using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ActivationParentContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-activation-parent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Parents.esm"); File.WriteAllBytes(path, Fixture());
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Parents.esm"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var player = records.RuntimeFormKey(0x14); var parent = Key(0x900); var child = Key(0x901);
            var effects = new List<FalloutReferenceScriptEffect>();
            FalloutReferenceScripts Scripts(FalloutReferenceWorld owner) => new(records, owner, new(records),
                new((_, _) => false, effects.Add));
            var scripts = Scripts(world);
            Require(scripts.Activate(child, player).Error is null && effects.Count == 0 && world.Get(child).Read(1) == 0,
                "Authored player suppression did not retain the closed child.");
            Require(!world.AllowsActivation(child, player) && world.AllowsActivation(child, parent) &&
                !world.IsActivationParent(child, Key(0x902)), "XAPD admission did not use the actual source XAPR relation.");
            world.ArmActivationChildren(parent, player);
            Require(world.AdvanceActivationRelays(.25f).Count == 0, "Positive source delay delivered too early.");
            world.ArmActivationChildren(parent, player);
            var ready = world.AdvanceActivationRelays(.25f);
            Require(ready.Count == 1 && ready[0].Child.Reference == child && ready[0].Child.ActionReference == parent &&
                ready[0].Child.DelaySeconds == .5f, "Repeated activation duplicated or restarted the parent's shared elapsed cycle.");
            Require(scripts.Activate(child, ready[0].Child.ActionReference).Error is null && effects.Single().Argument == parent &&
                effects[0].Target == child && world.Get(child).Read(1) == 1, "Relay lost the parent action reference or authored default branch.");
            world.ConsumeActivationRelay(ready[0]);
            Require(world.AdvanceActivationRelays(0).Count == 0, "Consumed delivery replayed before the next threshold.");
            var second = world.AdvanceActivationRelays(.25f).Single();
            Require(second.Child.Reference == Key(0x903), "Independent child threshold changed.");
            world.ConsumeActivationRelay(second);
            Require(world.Get(parent).ActivationRelay is { ElapsedSeconds: 0, Children.Count: 0 } &&
                world.AdvanceActivationRelays(10).Count == 0, "Settled parent clock did not reset or replayed a source activation.");

            world.ArmActivationChildren(parent, player); world.AdvanceActivationRelays(.25f);
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            Require(JsonSerializer.Serialize(cold.Get(parent).ActivationRelay) == JsonSerializer.Serialize(world.Get(parent).ActivationRelay),
                "Cold loading changed the authored thresholds, generation or elapsed clock.");
            var coldReady = cold.AdvanceActivationRelays(.25f).Single();
            Require(coldReady.Child.ActionReference == parent && Scripts(cold).Activate(child, parent).Error is null &&
                cold.Get(child).Read(1) == 2, "Cold delivery lost its single remaining source action.");
            cold.ConsumeActivationRelay(coldReady); Require(cold.AdvanceActivationRelays(0).Count == 0, "Cold receipt replayed.");
            var stale = world.AdvanceActivationRelays(.25f).Single();
            world.ArmActivationChildren(parent, player); world.ConsumeActivationRelay(stale);
            Require(world.Get(parent).ActivationRelay!.Children.Single(value => value.Reference == child).Revision > stale.Child.Revision,
                "Consuming an old admission erased a newer source rearm.");
            world.UnloadCell(cell.Cell.FormKey);
            var suspended = JsonSerializer.Serialize(world.Get(parent).ActivationRelay);
            Require(world.AdvanceActivationRelays(5).Count == 0 && JsonSerializer.Serialize(world.Get(parent).ActivationRelay) == suspended,
                "Unloaded parent advanced a nonexistent native update owner.");
            world.LoadCell(cell); world.AdvanceActivationRelays(.25f);
            var due = world.Get(parent).ActivationRelay!.Children.Single();
            Require(due.Due && due.Reference == Key(0x903), "Warm reuse lost the next crossing or replayed an already crossed rearm threshold.");
            world.Get(due.Reference).ScriptError = "GameMode: retained source failure.";
            var calls = world.Get(due.Reference).Read(1); var priorEffects = effects.Count;
            Require(scripts.Activate(due.Reference, parent).Error == world.Get(due.Reference).ScriptError && world.Get(due.Reference).Read(1) == calls &&
                effects.Count == priorEffects, "Activation parent cleared or replayed a stopped invocation.");
            world.ConsumeActivationRelay(new(parent, due));
            var corrupted = saved.Select(value => value.Reference == parent ? value with
            {
                ActivationRelay = value.ActivationRelay! with { SourceSha256 = new string('0', 64) }
            } : value).ToArray();
            using var rejected = new FalloutReferenceWorld(records); Reject(() => rejected.Restore(corrupted));
            Require(rejected.InstanceCount == 0 && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),
                "Source-drift refusal mutated the world or owned bytes.");
            Console.WriteLine("OPENNV_ACTIVATION_PARENT_CONTRACT_PASS sourceDelay=true parentAction=true sameCycleRearm=true " +
                "noReplay=true newerMarkPreserved=true cold=true unloadedClockSuspended=true faultRetained=true sourceDriftRefused=true");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static byte[] Fixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        byte[] Parent(uint id, float delay) => Join(Field("NAME", BitConverter.GetBytes(2u)), Field("DATA", new byte[24]),
            Field("XAPR", Join(BitConverter.GetBytes(id), BitConverter.GetBytes(delay))), Field("XAPD", [1]));
        var children = Join(Record("REFR", 0x900, Field("NAME", BitConverter.GetBytes(1u)), Field("DATA", new byte[24])),
            Record("REFR", 0x901, Parent(0x900, .5f)),
            Record("REFR", 0x902, Field("NAME", BitConverter.GetBytes(1u)), Field("DATA", new byte[24])),
            Record("REFR", 0x903, Parent(0x900, .75f)));
        var group = new byte[24 + children.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); children.CopyTo(group, 24);
        var scriptHeader = new byte[20]; UInt(scriptHeader, 12, 1);
        var local = new byte[24]; UInt(local, 0, 1);
        return Join(Record("TES4", 0, Field("HEDR", header)), Record("ACTI", 1),
            Record("DOOR", 2, Field("SCRI", BitConverter.GetBytes(0x50u))),
            Record("SCPT", 0x50, Field("SCHR", scriptHeader), Field("SLSD", local), Field("SCVR", Text("calls")),
                Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Text("begin OnActivate\nif IsActionRef player == 1\nelse\nset calls to calls + 1\nActivate\nendif\nend"))),
            Record("CELL", 0x800, Field("DATA", [1])), group);
    }
    private static FalloutFormKey Key(uint id) => new("Parents.esm", id);
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid source activation receipt was accepted.");
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string signature, byte[] payload)
    {
        var bytes = new byte[6 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)payload.Length)); payload.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)payload.Length); UInt(bytes, 12, id); payload.CopyTo(bytes, 24); return bytes;
    }
}
