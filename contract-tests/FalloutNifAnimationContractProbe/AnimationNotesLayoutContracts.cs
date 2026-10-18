using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class AnimationNotesLayoutContracts
{
    internal static void Run()
    {
        var sequence = Bytes(w =>
        {
            w.Write(0); w.Write(1); w.Write(0);
            w.Write(1); w.Write(-1); w.Write((byte)7);
            w.Write(1); w.Write(-1); w.Write(2); w.Write(-1); w.Write(-1);
            w.Write(1f); w.Write(-1); w.Write(2U); w.Write(1f);
            w.Write(.25f); w.Write(1.25f); w.Write(-1); w.Write(1);
            // Stream 27 ends in a four-byte animation-notes reference,
            // unlike the two-byte trailing field of stream 30 and later.
            w.Write(2);
        });
        var interpolator = Bytes(w =>
        {
            foreach (var v in new[] { 2f, -3f, 4f, 1f, 0f, 0f, 0f, 1f }) w.Write(v);
            w.Write(-1);
        });
        var reads = new List<FalloutNifReadRange>();
        var bytes = File(sequence, interpolator);
        var source = FalloutNifFile.Read(bytes, reads.Add);
        foreach (var block in source.Blocks)
        {
            reads.Clear(); _ = source.ReadObject(block.Index);
            Require(reads.Sum(read => read.Length) == block.Size && reads.First().Offset == block.Offset &&
                reads.Zip(reads.Skip(1)).All(pair => pair.First.Offset + pair.First.Length == pair.Second.Offset),
                "Stream 27 block extents were not consumed exactly.");
        }
        var clip = source.ReadControllerSequence(0);
        source.RequireNoActiveAnimationNotes(clip);
        Require(clip.AnimationNotes == 2 && source.ReadObject(2) is FalloutNifAnimationNotes { Notes.Count: 0 } &&
            clip.UnknownShort is null && clip.CycleType == 2 &&
            clip.StartTime == .25f && clip.StopTime == 1.25f && clip.ControlledBlocks.Single().Priority == 7,
            "Stream 27 lost its notes, timing or controlled-channel identity.");
        var pose = new FalloutNifAnimationSampler(source, clip.ControlledBlocks.Single().Interpolator).Sample(.75f);
        Require(pose.Translation == new FalloutNifVector3(2, -3, 4) && pose.Scale == 1,
            "Stream 27 transform channel was not retained.");
        Reject(() => FalloutNifFile.Read(File(sequence[..^2], interpolator)).ReadControllerSequence(0));
        Reject(() => FalloutNifFile.Read(File([.. sequence, 0, 0], interpolator)).ReadControllerSequence(0));
        var badReference = sequence.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(badReference.AsSpan(badReference.Length - 4), 3);
        Reject(() => FalloutNifFile.Read(File(badReference, interpolator)).ReadControllerSequence(0));
        Reject(() => FalloutNifFile.Read(File(sequence, interpolator, [0])).ReadObject(2));
        Reject(() => FalloutNifFile.Read(File(sequence, interpolator, [0, 0, 0])).ReadObject(2));
        Reject(() => source.RequireNoActiveAnimationNotes(clip with { AnimationNotes = 1 }));
        var activeNotes = FalloutNifFile.Read(File(sequence, interpolator, [1, 0, 0, 0, 0, 0]));
        RejectUnowned(() => activeNotes.RequireNoActiveAnimationNotes(activeNotes.ReadControllerSequence(0)));
        var unknown = bytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(unknown.AsSpan(52), 28);
        Reject(() => FalloutNifFile.Read(unknown));
        Console.WriteLine("OPENNV_ANIMATION_NOTES_LAYOUT_PASS stream=27 exactExtents=true finiteClock=true transform=true wrongWidthRefused=true unknownStreamRefused=true");
    }

    internal static void Audit(ReadOnlyMemory<byte> bytes)
    {
        var reads = new List<FalloutNifReadRange>();
        var source = FalloutNifFile.Read(bytes, reads.Add);
        foreach (var block in source.Blocks)
        {
            reads.Clear(); _ = source.ReadObject(block.Index);
            Require(reads.Sum(read => read.Length) == block.Size && reads.First().Offset == block.Offset &&
                reads.Zip(reads.Skip(1)).All(pair => pair.First.Offset + pair.First.Length == pair.Second.Offset),
                "Owned animation block extent differs from its field decode.");
        }
        foreach (var root in source.Roots) source.RequireNoActiveAnimationNotes(source.ReadControllerSequence(root));
        Console.WriteLine($"OPENNV_OWNED_ANIMATION_LAYOUT_PASS stream={source.UserVersion2} blocks={source.Blocks.Count} exactExtents=true notes=empty-or-absent sha256={source.Sha256}");
    }

    private static byte[] File(byte[] sequence, byte[] interpolator, byte[]? notes = null) => Bytes(w =>
    {
        void Text(string value) { w.Write(value.Length); w.Write(Encoding.ASCII.GetBytes(value)); }
        w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
        w.Write(3); w.Write(27U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        w.Write((ushort)3); Text("NiControllerSequence"); Text("NiTransformInterpolator"); Text("BSAnimNotes");
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2);
        w.Write(sequence.Length); w.Write(interpolator.Length); w.Write(notes?.Length ?? 2);
        w.Write(3); w.Write(21); Text("response"); Text("root"); Text("NiTransformController"); w.Write(0);
        w.Write(sequence); w.Write(interpolator); w.Write(notes ?? [0, 0]); w.Write(1); w.Write(0);
    });
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action read)
    {
        try { read(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed or unknown animation layout was admitted.");
    }
    private static void RejectUnowned(Action read)
    {
        try { read(); } catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Nonempty animation notes were ignored.");
    }
}
