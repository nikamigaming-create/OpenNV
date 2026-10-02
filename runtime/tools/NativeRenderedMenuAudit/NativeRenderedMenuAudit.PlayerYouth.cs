using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

public partial class NativeRenderedMenuAudit
{
    private void PlayerYouth(string baseRoot, string mod, string root, string[] dependencies)
    {
        var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        try
        {
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            var contract = FalloutNativeRaceSexResolver.Resolve(records);
            var session = new FalloutScriptSession();
            var sourceHash = SHA256.HashData(records.GetEffective(contract.Player).ReadData());
            var built = 0;
            foreach (var female in new[] { false, true })
            {
                var selection = contract.ForSex(female);
                var current = FalloutNativeCharacterCreation.ActorState(records, contract.Player, selection);
                var race = records.GetEffective(current.Race!.Value);
                var defaults = race.ReadSubrecords().Single(row => row.Signature == "DNAM").Data.Span;
                if (defaults.Length != 8) throw new InvalidDataException("Owned race default hair extent is invalid.");
                var defaultHair = race.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(defaults[(female ? 4 : 0)..]));
                var adult = FalloutNpcAppearanceResolver.Resolve(records, contract.Player, equippedArmor: [], appearanceState: current);
                session.SetPlayerYoung(true);
                var cold = new FalloutScriptSession();
                cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!);
                var young = FalloutNpcAppearanceResolver.Resolve(records, contract.Player, equippedArmor: [],
                    appearanceState: current with { PlayerYoung = cold.PlayerYoung });
                if (young.Hair != defaultHair || young.Models.Any(part => part.Role == "head-addon") || young.Race != adult.Race ||
                    young.RaceHeight != adult.RaceHeight || !young.FaceGen.SymmetricGeometry.AsSpan().SequenceEqual(adult.FaceGen.SymmetricGeometry))
                    throw new InvalidDataException("Owned youth policy changed race/face/height or lost source default hair/attachment suppression.");
                foreach (var first in new[] { false, true })
                {
                    var body = new RuntimeNativePlayerActor(records, source, young, null, first,
                        OpenNV.Runtime.RuntimeConfiguration.Load().World.GameUnitsToMeters, new Color(.4f, .4f, .4f));
                    try
                    {
                        AddChild(body);
                        if (body.Error is not null || body.Skeleton.Node.GetBoneCount() == 0) throw new InvalidDataException(body.Error ?? "Owned youth body has no bones.");
                        built++;
                    }
                    finally { body.Free(); }
                }
                session.SetPlayerYoung(false);
                var restored = FalloutNpcAppearanceResolver.Resolve(records, contract.Player, equippedArmor: [],
                    appearanceState: current with { PlayerYoung = session.PlayerYoung });
                if (restored.Hair != adult.Hair || JsonSerializer.Serialize(restored.Models) != JsonSerializer.Serialize(adult.Models))
                    throw new InvalidDataException("Clearing youth lost the original owned appearance.");
            }
            if (!sourceHash.AsSpan().SequenceEqual(SHA256.HashData(records.GetEffective(contract.Player).ReadData())))
                throw new InvalidDataException("Youth fixture changed owned player bytes.");
            GD.Print("OPENNV_NATIVE_PLAYER_YOUTH_PASS " + JsonSerializer.Serialize(new
            {
                sexSpecificDefaultHair = true,
                attachmentsSuppressed = true,
                raceFaceHeightPreserved = true,
                cold = true,
                clearedAppearanceRestored = true,
                firstAndThirdBodies = built,
                sourceReadonly = true,
                recording = false,
                retainedFrames = 0,
                boundary = "isolated-source-body-policy;child-race-and-campaign-retail-XR-acceptance-unverified"
            }));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
