using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseEmptyManagedSequences()
    {
        var nif = FalloutNifFile.Read(EmptyManagedSequenceFixture());
        var prototype = new RuntimeNativeNifPrototype(nif, .02f);
        Node3D? first = null; Node3D? second = null;
        try
        {
            first = prototype.Instantiate(); second = prototype.Instantiate();
            first.ProcessMode = second.ProcessMode = ProcessModeEnum.Disabled;
            AddChild(first); AddChild(second);
            var player = first.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var cold = second.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var original = prototype.Scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var pose = Transforms(first); var otherPose = Transforms(second);
            var mesh = first.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
            var vertices = mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var material = mesh.Mesh.SurfaceGetMaterial(0);
            RequireEmptySequence(prototype.Scene.Surfaces == 1 && prototype.Scene.Vertices == 3 && prototype.Scene.Triangles == 1 &&
                player.ActiveSequence == "Loop" && cold.ActiveSequence == "Loop" && player.SourceTimeSeconds == 2,
                "An empty managed sequence lost its real surface or invented a source selection.");
            var events = new List<FalloutNifTextKeyEvent>(); var coldEvents = new List<FalloutNifTextKeyEvent>();
            player.TextKeyHandler = key => { events.Add(key); return "audit-source-key"; };
            cold.TextKeyHandler = key => { coldEvents.Add(key); return "audit-source-key"; };
            player.RequestSourceSequence("Loop", 1); player._Process(.5);
            RequireEmptySequence(player.SourceTimeSeconds == 2.5 && cold.SourceTimeSeconds == 2 && original.SourceTimeSeconds == 2 &&
                events.Select(key => key.Text).SequenceEqual(["start"]) && cold.TextKeyCount == 0 && original.TextKeyCount == 0,
                "Zero-channel clocks or source events are shared between instances.");
            var state = JsonSerializer.Deserialize<FalloutObjectAnimationSnapshot>(JsonSerializer.Serialize(player.CaptureScriptState()))!;
            cold.RestoreScriptState(state);
            player._Process(2); cold._Process(2);
            RequireEmptySequence(player.SourceTimeSeconds == 2.5 && cold.SourceTimeSeconds == player.SourceTimeSeconds &&
                events.Select(key => key.Text).SequenceEqual(["start", "end", "start"]) &&
                coldEvents.SequenceEqual(events.Skip(1)), "Empty sequence cold state replayed its consumed start or lost a loop boundary.");
            var count = events.Count; player._Process(0); player.SeekSourceTime(3.25); player._Process(0);
            RequireEmptySequence(events.Count == count && player.SourceTimeSeconds == 3.25,
                "A zero advance or seek replayed empty-sequence text keys.");
            player.RequestSourceSequence("Finite", 1); player._Process(5);
            RequireEmptySequence(!player.Playing && !player.IsProcessing() && player.SourceTimeSeconds == 11 &&
                events.Skip(count).Select(key => key.Text).SequenceEqual(["start", "end"]),
                "An empty finite sequence failed to finish at its source stop.");
            var finished = player.CaptureScriptState()!; var coldCount = coldEvents.Count;
            cold.RestoreScriptState(finished); cold._Process(0);
            RequireEmptySequence(!cold.Playing && !cold.IsProcessing() && coldEvents.Count == coldCount,
                "Cold restoration restarted a completed empty sequence.");
            var unchanged = JsonSerializer.Serialize(cold.CaptureScriptState());
            RejectEmptySequence(() => cold.RestoreScriptState(finished with { Sha256 = new string('b', 64) }));
            RejectEmptySequence(() => cold.RestoreScriptState(finished with { Controller = 99 }));
            RejectEmptySequence(() => cold.RestoreScriptState(finished with { Sequence = "Absent" }));
            RequireEmptySequence(JsonSerializer.Serialize(cold.CaptureScriptState()) == unchanged &&
                Transforms(first).SequenceEqual(pose) && Transforms(second).SequenceEqual(otherPose) &&
                mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().SequenceEqual(vertices) &&
                mesh.Mesh.SurfaceGetMaterial(0) == material && original.TextKeyCount == 0,
                "An empty sequence or rejected cold state changed its geometry, material or another clock.");
            foreach (var invalid in new[] { "manager", "palette", "keys", "cycle", "range", "frequency", "name", "channel" })
                RejectEmptySequence(() =>
                {
                    var admitted = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(EmptyManagedSequenceFixture(invalid)), .02f);
                    admitted.Root.Free();
                });
            GD.Print("OPENNV_EMPTY_MANAGED_SEQUENCE_PASS fullNif=true sourceSurface=true loopKeys=true finiteCompletion=true instanceIsolation=true coldConsumedKeys=true sourceDriftRejected=true malformedBindingsRejected=true pixels=unverified");
        }
        finally { first?.Free(); second?.Free(); prototype.Scene.Root.Free(); }
    }

    private static void RequireEmptySequence(bool value, string message)
    { if (!value) throw new InvalidDataException(message); }

    private static void RejectEmptySequence(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Malformed empty managed sequence was admitted.");
    }

    private static byte[] EmptyManagedSequenceFixture(string invalid = "")
    {
        var templateBytes = SurfaceFixture(0x80000000);
        var template = FalloutNifFile.Read(templateBytes);
        var blocks = template.Blocks.Select(block => (Type: block.TypeName,
            Data: templateBytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        BinaryPrimitives.WriteInt32LittleEndian(blocks[0].Data.AsSpan(8), 6);
        void Time(BinaryWriter w, int next, ushort flags)
        {
            w.Write(next); w.Write(flags); w.Write(1f); w.Write(0f);
            w.Write(float.MaxValue); w.Write(float.MinValue); w.Write(0);
        }
        byte[] Sequence(int name, uint cycle, float start, float stop, int keys, bool channel = false) => Bytes(w =>
        {
            w.Write(name); w.Write(channel ? 1 : 0); w.Write(0);
            if (channel)
            {
                w.Write(-1); w.Write(7); w.Write((byte)0);
                w.Write(0); w.Write(-1); w.Write(5); w.Write(-1); w.Write(-1);
            }
            w.Write(1f); w.Write(keys); w.Write(cycle); w.Write(invalid == "frequency" ? 0f : 1f);
            w.Write(start); w.Write(stop); w.Write(invalid == "manager" ? 7 : 6); w.Write(0); w.Write((ushort)0);
        });
        byte[] Keys(float start, float stop) => Bytes(w =>
        { w.Write(-1); w.Write(2); w.Write(start); w.Write(3); w.Write(stop); w.Write(4); });
        blocks.Add(("NiControllerManager", Bytes(w =>
        {
            Time(w, 7, 0x004c); w.Write(false); w.Write(2); w.Write(8); w.Write(9);
            w.Write(invalid == "palette" ? 7 : 12);
        })));
        blocks.Add(("NiMultiTargetTransformController", Bytes(w =>
        { Time(w, -1, 0x006c); w.Write((ushort)1); w.Write(1); })));
        blocks.Add(("NiControllerSequence", Sequence(1, invalid == "cycle" ? 1U : 0U, 2,
            invalid == "range" ? 2 : 4, invalid == "keys" ? -1 : 10, invalid == "channel")));
        blocks.Add(("NiControllerSequence", Sequence(invalid == "name" ? 1 : 2, 2, 10, 11, 11)));
        blocks.Add(("NiTextKeyExtraData", Keys(2, 4)));
        blocks.Add(("NiTextKeyExtraData", Keys(10, 11)));
        blocks.Add(("NiDefaultAVObjectPalette", Bytes(w =>
        { w.Write(0); w.Write(1); w.Write(7); w.Write("surface"u8); w.Write(1); })));
        string[] names = ["surface", "Loop", "Finite", "start", "end", "UnownedController"];
        return Bytes(w =>
        {
            w.Write("Gamebryo File Format, Version 20.2.0.7\n"u8);
            w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
            w.Write(blocks.Count); w.Write(34U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            w.Write((ushort)blocks.Count);
            foreach (var block in blocks) { w.Write(block.Type.Length); w.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Count; index++) w.Write((ushort)index);
            foreach (var block in blocks) w.Write(block.Data.Length);
            w.Write(names.Length); w.Write(names.Max(name => name.Length));
            foreach (var name in names) { w.Write(name.Length); w.Write(Encoding.ASCII.GetBytes(name)); }
            w.Write(0);
            foreach (var block in blocks) w.Write(block.Data);
            w.Write(1); w.Write(0);
        });
    }
}
