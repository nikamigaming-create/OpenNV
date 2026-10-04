using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedHeadingProbe
{
    internal static void Run(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var trigger = records.GetEffective(new("Fallout3.esm", 0x0c6de2));
        var source = records.GetEffective(new("Fallout3.esm", 0x0c6de0));
        var hashes = new[] { trigger, source }.Select(record => (Record: record, Hash: SHA256.HashData(record.ReadData()))).ToArray();
        var bindings = new FalloutScriptBindings(records, trigger, source, source.ReadSubrecords());
        var goal = bindings.Reference("CG02JonasREF");
        var dadReady = bindings.Variable("CG02.DadReady");
        var jonasReady = bindings.Variable("CG02.JonasReady");
        var playerReady = bindings.Variable("CG02.PlayerReady");
        var angle = bindings.Variable("angle");
        var player = records.RuntimeFormKey(0x14);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(trigger) ??
            throw new InvalidDataException("Original photo trigger has no owned cell.")));
        var target = world.Placement(goal);
        var quests = new FalloutQuestState(records);
        var pose = target with { Position = [target.Position[0], target.Position[1] - 10, target.Position[2]], RotationRadians = [0, 0, 0] };
        float Query(FalloutFormKey caller, FalloutFormKey destination)
        {
            if (caller != player || destination != goal) throw new InvalidDataException("Original photo queried a different caller/target.");
            return world.HeadingAngle(caller, destination, pose, 1f / 70);
        }
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidDataException("Isolated photo trigger emitted another effect."), HeadingAngle: Query));
        Check(0, true, true, true);
        Check(44, true, true, true);
        Check(-44, true, true, true);
        Check(46, true, true, false);
        Check(-46, true, true, false);
        Check(0, false, true, false);
        Check(0, true, false, false);
        if (hashes.Any(pair => !pair.Hash.SequenceEqual(SHA256.HashData(pair.Record.ReadData()))))
            throw new InvalidDataException("Photo guard fixture changed winning source bytes.");
        Console.WriteLine($"OPENNV_OWNED_HEADING_PASS script={source.FormKey} trigger={trigger.FormKey} target={goal} " +
            "originalProgram=true actorReadyGuards=true playerFacing=true rejectedBearing=true leaveClears=true sourceReadonly=true " +
            "fixture=isolated-original-photo-trigger physicalContactAndCampaignAndRetailParity=unverified");

        void Check(float degrees, bool dad, bool jonas, bool expected)
        {
            quests.SetVariable(dadReady.Owner, dadReady.Index, dad ? 1 : 0);
            quests.SetVariable(jonasReady.Owner, jonasReady.Index, jonas ? 1 : 0);
            quests.SetVariable(playerReady.Owner, playerReady.Index, 0);
            pose = pose with { RotationRadians = [0, 0, degrees * MathF.PI / 180] };
            var result = scripts.DispatchFrame(trigger.FormKey, [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { player })], .1).Single();
            if (result.Error is not null || (quests.Variable(playerReady.Owner, playerReady.Index) == 1) != expected ||
                dad && jonas && MathF.Abs((float)world.Get(trigger.FormKey).Read(angle.Index) + degrees) > .001f)
                throw new InvalidDataException("Original photo trigger changed readiness or relative heading.");
            var savedWorld = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(savedWorld);
            if (Math.Abs(cold.Get(trigger.FormKey).Read(angle.Index) - world.Get(trigger.FormKey).Read(angle.Index)) > .0001)
                throw new InvalidDataException("Photo trigger lost its source local on cold reference restoration.");
            result = scripts.DispatchFrame(trigger.FormKey, [new("OnTriggerLeave", player)], .1).Single();
            if (result.Error is not null || quests.Variable(playerReady.Owner, playerReady.Index) != 0)
                throw new InvalidDataException("Original contact departure did not clear player readiness.");
        }
    }
}
