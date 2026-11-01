using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    // Visual ownership does not silently claim the independent source sound lane.
    private void RecordNativeLightSoundBoundary(Node3D model, FalloutPlacedReference reference,
        FalloutBaseObjectDefinition definition)
    {
        var soundSources = new HashSet<string>(StringComparer.Ordinal);
        var record = _nativePluginStack!.GetEffective(definition.FormKey);
        var sound = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "SNAM").Data;
        if (!sound.IsEmpty)
        {
            if (sound.Length != 4) throw new InvalidDataException("LIGH SNAM has an invalid FormID extent.");
            if (record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(sound.Span)) is { } form)
                soundSources.Add($"LIGH:{form}");
        }
        foreach (var node in new[] { model }.Concat(model.FindChildren("*", "", true, false).OfType<Node3D>()))
            if (node.HasMeta("opennv_addon_audio_unbound"))
                soundSources.Add($"ADDN:{node.GetMeta("opennv_addon_audio_unbound").AsString()}");
        if (soundSources.Count != 0)
            _nativeReferenceDivergences[reference.FormKey.ToString()] =
                $"Source LIGH model/light visual is bound; persistent source sound lifetime is unbound: {string.Join(",", soundSources.Order(StringComparer.Ordinal))}.";
    }
}
