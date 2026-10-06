using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeDefaultActivationAudit
{
    private void ExerciseSynthetic()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-default-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var root = new Node3D(); AddChild(root);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Default.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Default.esm"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var source = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Unexpected synthetic script effect.")));
            foreach (var reference in cell.References)
            {
                Require(source.Dispatch(reference.FormKey, "GameMode").Error is not null, "Synthetic source failure was not reached.");
                _ = world.Inventory(reference.FormKey, 1);
            }
            var body = new Dictionary<FalloutFormKey, StaticBody3D>();
            foreach (var reference in cell.References)
            {
                var node = new Node3D(); node.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
                var collider = new StaticBody3D(); collider.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
                node.AddChild(collider); root.AddChild(node); body.Add(reference.FormKey, collider);
            }
            var reports = new List<string>(); var completed = new List<bool>();
            var defaults = 0; var opened = 0; var begins = 0; var ends = 0; var unavailable = false;
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = reports.Add };
            events.Configure(records, world, new(records), cell, root, new((_, _) => false, effect =>
            {
                Require(effect.Kind == FalloutReferenceEffectKind.DefaultActivate, "Synthetic activation emitted an unrelated effect.");
                defaults++; events.DefaultActivate(effect.Target ?? effect.Source, effect.Argument);
            }), _ => Transform3D.Identity, 1, 1);
            events.Interact = (reference, node, signature, actor) =>
            {
                Require(reference.FormKey == Key(0x900) && node == body[Key(0x900)].GetParent() && signature == "CONT" &&
                    actor == records.RuntimeFormKey(0x14), "Default action lost its actual native/reference binding.");
                if (unavailable) throw new NotSupportedException("Synthetic container owner unavailable.");
                opened++;
            };
            events.ObservePlayerActivationBegin = _ => begins++;
            events.ObservePlayerActivationEnd = (_, success) => { Require(success, "Completed activation ended unsuccessfully."); ends++; };
            events.ObservePlayerActivationFinished = (_, success) => completed.Add(success);
            root.AddChild(events); events.SetProcess(false);
            var stopped = JsonSerializer.Serialize(world.Get(Key(0x900)).Capture());
            Require(events.CanAdmitIndependentDefaultInteraction(Key(0x900)) &&
                JsonSerializer.Serialize(world.Get(Key(0x900)).Capture()) == stopped,
                "Resident independent-default observation acknowledged a source failure or lost its native owner.");
            Require(events.TryActivate(body[Key(0x900)]) && !events.TryActivate(body[Key(0x900)]), "Pending activation was rejected or duplicated.");
            Require(events.CanAdmitIndependentDefaultInteraction(Key(0x900)) && defaults == 0,
                "Observing the existing independent activation consumed its queue or reinstated the unrelated script blocker.");
            Pump(events); Pump(events);
            Require(defaults == 1 && opened == 1 && begins == 1 && ends == 1 && completed.SequenceEqual([true]) &&
                JsonSerializer.Serialize(world.Get(Key(0x900)).Capture()) == stopped,
                "Queued native default activation replayed a prefix, lost completion or repeated without input.");
            Require(events.TryActivate(body[Key(0x900)]), "Fresh independent activation was suppressed."); Pump(events);
            Require(defaults == 2 && opened == 2 && completed.SequenceEqual([true, true]), "Fresh input did not dispatch exactly one action.");
            unavailable = true;
            Require(events.TryActivate(body[Key(0x900)]), "Unavailable owner did not reach explicit default refusal."); Pump(events);
            Require(defaults == 3 && opened == 2 && completed.SequenceEqual([true, true, false]) && begins == 3 && ends == 2 &&
                reports.Count(report => report.Contains("container owner unavailable", StringComparison.Ordinal)) == 1 &&
                JsonSerializer.Serialize(world.Get(Key(0x900)).Capture()) == stopped,
                "A fresh default failure hid its error or poisoned the stopped source invocation.");
            foreach (var id in new uint[] { 0x901, 0x902, 0x903 })
            {
                Require(!events.CanAdmitIndependentDefaultInteraction(Key(id)), "Authored or unparsed script acquired independent bot fault scope.");
                Require(!events.TryActivate(body[Key(id)]), "Authored/unknown activation acquired the independent default path.");
            }
            world.Get(Key(0x900)).Enabled = false;
            Require(!events.CanAdmitIndependentDefaultInteraction(Key(0x900)), "Disabled reference acquired independent bot fault scope.");
            Require(!events.TryActivate(body[Key(0x900)]), "Disabled native reference admitted input.");
            world.Get(Key(0x900)).Enabled = true;
            events.SetResidency(cell with { References = [] }, root);
            Require(!events.CanAdmitIndependentDefaultInteraction(Key(0x900)), "Unresident reference acquired independent bot fault scope.");
            Require(!events.TryActivate(body[Key(0x900)]), "Unresident native binding admitted input.");
            GD.Print("OPENNV_NATIVE_DEFAULT_QUEUE_PASS duplicatePending=false idleReplay=false freshInput=true " +
                "completionIndependent=true retainedFaultAndPrefix=true failedDefaultVisible=true unknownAndAuthoredGuarded=true enabledAndResidency=true");
        }
        finally { root.Free(); Directory.Delete(directory, true); }
    }

    private static byte[] Fixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var refs = Enumerable.Range(0, 4).Select(index => Record("REFR", (uint)(0x900 + index),
            Field("NAME", BitConverter.GetBytes((uint)(index + 1))), Field("DATA", new byte[24]))).SelectMany(value => value).ToArray();
        var group = new byte[24 + refs.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); refs.CopyTo(group, 24);
        const string fail = "begin GameMode\nset prefix to prefix + 1\nMissingOperation\nend";
        return Join(Record("TES4", 0, Field("HEDR", header)),
            Enumerable.Range(0, 4).Select(index => Record("CONT", (uint)(index + 1), Field("EDID", Text("DefaultBase" + index)),
                Field("SCRI", BitConverter.GetBytes((uint)(0x50 + index))))).SelectMany(value => value).ToArray(),
            Script(0x50, fail), Script(0x51, fail + "\nbegin OnActivate\nset prefix to 99\nend"),
            Script(0x52, "begin GameMode\nset prefix to 1"), Script(0x53, null), Record("CELL", 0x800, Field("DATA", [1])), group);
    }
    private static byte[] Script(uint id, string? source)
    {
        var header = new byte[20]; UInt(header, 12, 1); var local = new byte[24]; UInt(local, 0, 1);
        return Record("SCPT", id, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("prefix")),
            source is null ? [] : Field("SCTX", Text(source)));
    }
    private static FalloutFormKey Key(uint id) => new("Default.esm", id);
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var payload = Join(fields); var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        UInt(result, 4, (uint)payload.Length); UInt(result, 12, id); payload.CopyTo(result, 24); return result;
    }
}
