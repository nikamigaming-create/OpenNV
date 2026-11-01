using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExercisePrimaryKfSelection()
    {
        var baseline = EmptyManagedSequenceFixture();
        byte[] TopObjects(params int[] roots)
        {
            var bytes = new byte[baseline.Length - 8 + 4 + roots.Length * 4];
            baseline.AsSpan(0, baseline.Length - 8).CopyTo(bytes);
            var cursor = baseline.Length - 8;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor), roots.Length);
            foreach (var root in roots)
            {
                cursor += 4;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor), root);
            }
            return bytes;
        }
        foreach (var roots in new[] { new[] { 8, 9 }, new[] { 9, 8 }, new[] { 8 } })
        {
            var bytes = TopObjects(roots); var hash = SHA256.HashData(bytes);
            var source = FalloutNifFile.Read(bytes);
            var primary = FalloutKfSequenceSource.Primary(source);
            if (primary.Block.Index != roots[0] || source.Roots.Count != roots.Length ||
                !SHA256.HashData(bytes).SequenceEqual(hash))
                throw new InvalidDataException("Actor KF selection changed top-object order, source data or primary identity.");
        }
        foreach (var roots in new[] { new[] { 0, 8 }, Array.Empty<int>() })
        {
            try { _ = FalloutKfSequenceSource.Primary(FalloutNifFile.Read(TopObjects(roots))); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { continue; }
            throw new InvalidDataException("Actor KF silently selected a later sequence or invented an absent primary.");
        }
        var unknownSecondary = TopObjects(8, 9);
        var needle = "NiControllerSequence"u8;
        var firstType = unknownSecondary.AsSpan().IndexOf(needle);
        if (firstType < 0) throw new InvalidDataException("Primary KF fixture lost its primary type declaration.");
        var secondRelative = unknownSecondary.AsSpan(firstType + needle.Length).IndexOf(needle);
        if (secondRelative < 0) throw new InvalidDataException("Primary KF fixture lost its secondary type declaration.");
        var secondType = secondRelative + firstType + needle.Length;
        unknownSecondary[secondType] = (byte)'Z'; unknownSecondary[secondType + 1] = (byte)'Z';
        var rejectedUnknownSecondary = false;
        try { _ = FalloutKfSequenceSource.Primary(FalloutNifFile.Read(unknownSecondary)); }
        catch (NotSupportedException) { rejectedUnknownSecondary = true; }
        if (!rejectedUnknownSecondary)
            throw new InvalidDataException("Actor KF skipped an unsupported secondary top-level object.");
        GD.Print("OPENNV_PRIMARY_KF_SELECTION_PASS firstTopObject=true secondaryPreserved=true reorderedPrimary=true invalidFirstRefused=true sourceReadonly=true");
    }
}
