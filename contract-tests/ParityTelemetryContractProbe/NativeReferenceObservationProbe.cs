using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics.Parity;

internal static class NativeReferenceObservationProbe
{
    internal static void Run()
    {
        var reference = new FalloutPlacedReference(new("Fixture.esm", 0x123), "Reference_é_\ud83d\ude80", new("Fixture.esm", 0x800),
            new("Fixture.esm", 0x321), 0x8000_0080, [-0f, float.Epsilon, BitConverter.UInt32BitsToSingle(0x7fc01234)],
            [float.NegativeInfinity, 0.5f, -2f], 1.25f, null, null, null, false);
        var model = new FalloutBaseObjectDefinition(reference.Base, "STAT", "Object_漢字_\ud800", "meshes/fixture.nif", null);
        foreach (var disposition in new[] { "source", "disabled", "resident-presentation", "source-primitive-contact-owner" })
            foreach (var source in new[] { model, model with { ModelPath = null } })
                if (!NativeReferenceObservation.Serialize(reference, source, disposition).SequenceEqual(Legacy(reference, source, disposition)))
                    throw new InvalidOperationException("Native reference observation changed canonical UTF-8, field order or Float32 bits.");
        var original = NativeReferenceObservation.Serialize(reference, model, "source");
        var moved = reference with { Position = [0f, reference.Position[1], reference.Position[2]] };
        if (original.SequenceEqual(NativeReferenceObservation.Serialize(moved, model, "source")))
            throw new InvalidOperationException("Native reference observation concealed a changed source Float32 bit.");
        _ = NativeReferenceObservation.Serialize(reference, model, "source");
        _ = Legacy(reference, model, "source");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 512; index++) _ = Legacy(reference, model, "source");
        var legacyBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 512; index++) _ = NativeReferenceObservation.Serialize(reference, model, "source");
        var currentBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (currentBytes >= legacyBytes) throw new InvalidOperationException("Single-buffer reference encoding did not reduce temporary allocations.");
        Console.WriteLine($"OPENNV_REFERENCE_OBSERVATION_CONTRACT_OK canonicalBytes=true unicode=true floatBits=true nullModel=true legacyBytes={legacyBytes} currentBytes={currentBytes}");
    }

    // The previous wire encoder remains an independent compatibility oracle.
    private static byte[] Legacy(FalloutPlacedReference reference, FalloutBaseObjectDefinition model, string disposition)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
        Text(reference.FormKey.ToString()); Text(reference.EditorId); Text(reference.Base.ToString());
        Text(model.Signature); Text(model.EditorId); Text(disposition);
        writer.Write(reference.Flags);
        foreach (var value in reference.Position) writer.Write(value);
        foreach (var value in reference.RotationRadians) writer.Write(value);
        writer.Write(reference.Scale); Text(model.ModelPath ?? string.Empty);
        writer.Flush(); return stream.ToArray();
    }
}
