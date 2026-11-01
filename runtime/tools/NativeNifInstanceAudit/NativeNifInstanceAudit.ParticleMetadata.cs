using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseParticleMetadata()
    {
        var accepted = ParticleMetadataFixture(invalid: false);
        var hash = SHA256.HashData(accepted);
        var file = FalloutNifFile.Read(accepted);
        var particle = (FalloutNifParticleSystem)file.ReadObject(0);
        if (file.ReadObject(particle.Geometry.ExtraData.Single()) is not FalloutNifStringExtraData)
            throw new InvalidDataException("Particle metadata fixture lost its actual typed source attachment.");
        var prototype = new RuntimeNativeNifPrototype(file, 1);
        try
        {
            if (prototype.Scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifParticleSystem>().Count() != 1 ||
                !SHA256.HashData(accepted).SequenceEqual(hash))
                throw new InvalidDataException("Validated particle metadata changed source bytes or suppressed its actual particle owner.");
        }
        finally { prototype.Scene.Root.Free(); }
        try
        {
            var refused = new RuntimeNativeNifPrototype(FalloutNifFile.Read(ParticleMetadataFixture(invalid: true)), 1);
            refused.Scene.Root.Free();
        }
        catch (NotSupportedException error) when (error.Message.Contains("unsupported extra-data", StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidDataException("Particle importer admitted an unowned visual attachment.");
    }

    private static byte[] ParticleMetadataFixture(bool invalid)
    {
        var bytes = DirectParticleGravityFixture("float");
        var source = FalloutNifFile.Read(bytes);
        var blocks = source.Blocks.Select(block => (block.TypeName,
            Bytes: bytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var names = source.Strings.Concat(new[] { "AuthoredMetadata", "KeepSourceGeometry" }).ToArray();
        var body = blocks[0].Bytes;
        if (BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)) != 0)
            throw new InvalidDataException("Particle metadata baseline already owns extra-data references.");
        var extra = blocks.Count;
        blocks[0] = ("NiParticleSystem", Bytes(writer =>
        {
            writer.Write(body.AsSpan(0, 4)); writer.Write(1); writer.Write(extra); writer.Write(body.AsSpan(8));
        }));
        blocks.Add(invalid ? ("NiNode", blocks[10].Bytes.ToArray()) :
            ("NiStringExtraData", Bytes(writer => { writer.Write(names.Length - 2); writer.Write(names.Length - 1); })));
        var result = WriteParticleShapeColorNif(blocks, names);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(result.Length - 4), source.Roots.Single());
        return result;
    }
}
