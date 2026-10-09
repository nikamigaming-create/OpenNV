using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutEngineCommandBooleanLeaf(string RuntimeSha256, uint SourceAddress,
    uint SourceRva, int BodyBytes, string BodySha256, bool Result, int Instructions);

// This is a source declaration reader, not an instruction executor. A returned
// declaration describes only the public Boolean result and absence of effects.
internal sealed class FalloutEngineCommandBooleanSource(string identity, uint imageBase,
    Func<uint, FalloutEngineCommandBooleanLeaf> inspect)
{
    private readonly Dictionary<uint, FalloutEngineCommandBooleanLeaf> _leaves = [];
    internal string RuntimeSha256 { get; } = identity;
    internal uint ImageBase { get; } = imageBase;
    internal FalloutEngineCommandBooleanLeaf Read(uint address)
    {
        if (_leaves.TryGetValue(address, out var retained)) return retained;
        var leaf = inspect(address);
        if (leaf.SourceAddress != address || leaf.SourceRva != address - ImageBase ||
            !StringComparer.OrdinalIgnoreCase.Equals(leaf.RuntimeSha256, RuntimeSha256))
            throw new InvalidDataException("Engine command leaf changed its exact retained executable source identity.");
        _leaves.Add(address, leaf); return leaf;
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutEngineCommandBooleanSource ReadEngineCommandBooleanSource(string path, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(path);
        var identity = Convert.ToHexString(SHA256.HashData(bytes));
        if (!StringComparer.OrdinalIgnoreCase.Equals(identity, expectedSha256))
            throw new InvalidDataException("Engine command executable changed before its declaration owner was constructed.");
        var (code, image) = Load(bytes);
        return new(identity, image.Base, address =>
        {
            if (!image.IsExecutableExtent(address) || address < image.CodeBase || address - image.CodeBase >= code.Length)
                throw new NotSupportedException("Borrowed command pointer has no complete decoded executable source owner.");
            var leaf = ReadEngineCommandBooleanLeaf(code, checked((int)(address - image.CodeBase)), identity, address, image.Base);
            for (var at = 0; at < leaf.BodyBytes; ++at)
                if (!image.IsExecutableExtent(checked(address + (uint)at)))
                    throw new InvalidDataException("Engine Boolean body leaves its complete decoded executable source extent.");
            return leaf;
        });
    }

    internal static FalloutEngineCommandBooleanLeaf ReadEngineCommandBooleanLeaf(byte[] code, int start,
        string runtimeSha256, uint address, uint imageBase)
    {
        if (runtimeSha256.Length != 64 || runtimeSha256.Any(value => !Uri.IsHexDigit(value)) ||
            start < 0 || start >= code.Length || address < imageBase)
            throw new InvalidDataException("Boolean command declaration has no exact source/entry extent.");
        byte? result = null; var framed = false; var prologue = false; var cursor = start; var instructions = 0;
        InterfaceInstruction? previous = null;
        while (instructions < 32 && cursor - start < 128)
        {
            var row = InterfaceDecode(code, cursor, previous); ++instructions;
            // Prefix semantics are not inferred from a decoded opcode alone.
            // In particular, LOCK on a register-only operation is not admitted.
            if (code[row.At] != row.Opcode || row.Next - start > 128)
                throw new NotSupportedException("Boolean command leaf has an unowned prefix or inspection extent.");
            switch (row.Opcode)
            {
                case 0x90: break; // x86 NOP
                case 0xb0: result = checked((byte)(row.Immediate ?? throw new InvalidDataException("Boolean immediate is absent."))); break;
                case 0xb8:
                    result = unchecked((byte)(row.Immediate ?? throw new InvalidDataException("Boolean immediate is absent."))); break;
                case 0x30:
                case 0x31:
                case 0x32:
                case 0x33:
                    if (row.ModRm != 0xc0) throw new NotSupportedException("Boolean command leaf reads another register or memory.");
                    result = 0; break;
                case 0x55:
                    if (instructions != 1 || prologue) throw new NotSupportedException("Boolean command leaf has an unowned stack effect.");
                    prologue = true; break;
                case 0x8b:
                case 0x89:
                    if (!prologue || framed || instructions != 2 || row.ModRm != (row.Opcode == 0x8b ? 0xec : 0xe5))
                        throw new NotSupportedException("Boolean command leaf reads arguments or changes an unowned register.");
                    framed = true; break;
                case 0x5d:
                case 0xc9:
                    if (!framed) throw new NotSupportedException("Boolean command leaf has an unmatched frame retirement.");
                    framed = false; prologue = false; break;
                case 0xc3:
                    if (framed || prologue || result is null or > 1)
                        throw new NotSupportedException("Boolean command leaf lacks a complete canonical cdecl return.");
                    var extent = row.Next - start;
                    return new(runtimeSha256, address, address - imageBase, extent,
                        Convert.ToHexString(SHA256.HashData(code.AsSpan(start, extent))), result == 1, instructions);
                default:
                    throw new NotSupportedException("Borrowed engine command requires its memory, call, branch or other source behavior owner.");
            }
            previous = row; cursor = row.Next;
            if (cursor >= code.Length) throw new InvalidDataException("Boolean command source ends before its complete return.");
        }
        throw new NotSupportedException("Boolean command leaf exceeds its explicit source inspection budget.");
    }
}
