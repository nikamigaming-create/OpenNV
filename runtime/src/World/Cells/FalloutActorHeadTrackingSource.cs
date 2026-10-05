using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutActorHeadTrackingSource
{
    internal static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));

    internal static void Validate(FalloutPluginStack records, FalloutFormKey actor, FalloutActorHeadTrackingSnapshot saved)
    {
        saved.Validate();
        var binding = saved.Binding;
        var reference = records.GetEffective(actor);
        var partSource = records.GetEffective(records.RuntimeFormKey(0x1d));
        if (binding.Actor != actor || reference.Signature != "ACHR" ||
            records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME")).Signature != "NPC_" ||
            !Hash(reference).Equals(binding.ActorSha256, StringComparison.OrdinalIgnoreCase) ||
            partSource.FormKey != binding.BodyPartSource || !Hash(partSource).Equals(binding.BodyPartSha256, StringComparison.OrdinalIgnoreCase) ||
            FalloutBodyPartLook.Read(partSource) != binding.Part)
            throw new InvalidDataException("Saved head actor or body-part source changed.");
        foreach (var target in saved.Targets.Slots.Select(slot => slot.Target).Append(saved.Targets.CachedTarget).OfType<FalloutFormKey>())
            if (target != records.RuntimeFormKey(0x14) && records.GetEffective(target).Signature is not ("REFR" or "ACHR" or "ACRE"))
                throw new InvalidDataException("Saved head target is not a source reference.");
        var content = RuntimeLiveContentSource.Current ?? throw new NotSupportedException("Saved head tracking has no owned content source.");
        if (FalloutLookSettings.Read(FalloutInstallationSettings.Read(content)) != binding.Settings ||
            FalloutGameSettingFloats.ReadRetained(records, "fAIHoldDefaultHeadTrackTimer", nameof(FalloutHeadTrackingState)) != saved.Targets.SourceHoldSeconds)
            throw new InvalidDataException("Saved head tracking differs from its source LookIK or hold settings.");
        if (!content.TryRead(binding.SkeletonResource, null, out var bytes, out _) ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(binding.SkeletonSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved head skeleton source changed or is missing.");
        var nif = FalloutNifFile.Read(bytes);
        var bones = new List<(int Block, string Name, int Parent)>();
        var visited = new HashSet<int>(); var names = new HashSet<string>(StringComparer.Ordinal);
        void Walk(int block, int parent)
        {
            if (!visited.Add(block)) throw new InvalidDataException("Saved head rig has repeated source ownership.");
            if (nif.ReadObject(block) is FalloutNifGeometry)
            {
                if (parent < 0) throw new InvalidDataException("Saved head rig geometry has no bone parent.");
                return;
            }
            var node = nif.ReadNode(block);
            if (string.IsNullOrWhiteSpace(node.Name) || !names.Add(node.Name))
                throw new InvalidDataException("Saved head rig has an unnamed or duplicate bone.");
            var index = bones.Count; bones.Add((block, node.Name, parent));
            foreach (var child in node.Children.Where(child => child >= 0)) Walk(child, index);
        }
        foreach (var root in nif.Roots) Walk(root, -1);
        if (binding.Part is null) return;
        var bone = binding.Bone!.Value; var parentIndex = binding.Parent!.Value;
        if (bone >= bones.Count || parentIndex >= bones.Count || bones[bone] != (binding.NifBlock!.Value, binding.BoneName!, parentIndex) ||
            bones[parentIndex].Name != binding.ParentName)
            throw new InvalidDataException("Saved head bone index, parent or source block changed.");
        var controllers = nif.Blocks.Where(block => block.TypeName == "NiFloatExtraDataController")
            .Select(block => (FalloutNifFloatExtraDataController)nif.ReadObject(block.Index))
            .Where(controller => controller.Time.Target == binding.NifBlock).ToArray();
        if (controllers.Length > 1 || controllers.SingleOrDefault()?.Block.Index != binding.OverrideController ||
            controllers.SingleOrDefault()?.ExtraDataName != binding.OverrideName)
            throw new InvalidDataException("Saved head override controller changed.");
        if (binding.OverrideName is { } name && nif.ReadNode(binding.NifBlock.Value).ExtraData.Where(index => index >= 0)
            .Select(nif.ReadObject).OfType<FalloutNifFloatExtraData>().Count(extra => extra.Name == name && float.IsFinite(extra.Value)) != 1)
            throw new InvalidDataException("Saved head override lost its declared scalar target.");
    }
}
