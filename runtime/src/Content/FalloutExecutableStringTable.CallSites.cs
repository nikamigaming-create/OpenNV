using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutEnginePointerGetter(uint Entry, int MemberOffset, int BodyBytes,
    string BodySha256, IReadOnlyList<FalloutEnginePointerGetter> Children);
internal sealed record FalloutEngineCallSite(string RuntimeSha256, uint ImageBase, uint Address,
    string DeclarationSha256, FalloutEnginePointerGetter Getter);

internal sealed class FalloutEngineCallSiteSource(string identity, Func<uint, FalloutEngineCallSite> inspect)
{
    private readonly Dictionary<uint, FalloutEngineCallSite> _sites = [];
    internal string RuntimeSha256 { get; } = identity;
    internal FalloutEngineCallSite Read(uint address)
    {
        if (_sites.TryGetValue(address, out var retained)) return retained;
        var source = inspect(address);
        if (source.Address != address || !StringComparer.OrdinalIgnoreCase.Equals(source.RuntimeSha256, RuntimeSha256))
            throw new InvalidDataException("Source call site changed its retained executable identity.");
        _sites.Add(address, source); return source;
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutEngineCallSiteSource ReadEngineCallSiteSource(string path, string expectedSha256)
    {
        var raw = File.ReadAllBytes(path); var identity = Convert.ToHexString(SHA256.HashData(raw));
        if (!StringComparer.OrdinalIgnoreCase.Equals(identity, expectedSha256))
            throw new InvalidDataException("Source call-site executable changed before declaration admission.");
        var (code, image) = Load(raw);
        return new(identity, address => ReadEngineCallSite(code, image.CodeBase, image.Base,
            identity, address, image.IsExecutableExtent));
    }

    // Inspect a declared near CALL and its complete pure, zero-argument thiscall
    // pointer getter. The result is a neutral field-read contract. Source bytes
    // never become executable host instructions or a persistent launch asset.
    internal static FalloutEngineCallSite ReadEngineCallSite(byte[] code, uint codeBase, uint imageBase,
        string runtimeSha256, uint address, Func<uint, bool> executable)
    {
        if (runtimeSha256.Length != 64 || !runtimeSha256.All(Uri.IsHexDigit) || address < imageBase)
            throw new InvalidDataException("Call site has no exact owned executable identity.");
        var at = CallSiteOffset(address, codeBase, code.Length, 5);
        for (var index = 0; index < 5; ++index)
            if (!executable(checked(address + (uint)index)))
                throw new InvalidDataException("Call declaration leaves its complete source executable extent.");
        if (code[at] != 0xe8)
            throw new NotSupportedException("Source mutation is not an admitted near CALL with a typed pointer-getter owner.");
        var target = unchecked(address + 5U + (uint)BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        var getter = ReadEnginePointerGetter(code, codeBase, target, executable, [], 0);
        return new(runtimeSha256, imageBase, address,
            Convert.ToHexString(SHA256.HashData(code.AsSpan(at, 5))), getter);
    }

    private readonly record struct CallSiteSymbol(bool PointerRead, int Offset);
    private static FalloutEnginePointerGetter ReadEnginePointerGetter(byte[] code, uint codeBase, uint entry,
        Func<uint, bool> executable, HashSet<uint> active, int depth)
    {
        if (depth >= 8 || !active.Add(entry))
            throw new NotSupportedException("Source getter has a recursive or uninspected call domain.");
        try
        {
            var start = CallSiteOffset(entry, codeBase, code.Length, 1); var cursor = start;
            CallSiteSymbol? eax = null, ecx = new(false, 0);
            var locals = new Dictionary<int, CallSiteSymbol?>(); var localWords = 0;
            var savedFrame = false; var frame = false; var frameRetiring = false;
            var children = new List<FalloutEnginePointerGetter>(); InterfaceInstruction? previous = null;
            CallSiteSymbol? Register(int register) => register switch
            { 0 => eax, 1 => ecx, _ => throw new NotSupportedException("Pure getter uses an unowned register domain.") };
            void RegisterWrite(int register, CallSiteSymbol? value)
            {
                if (register == 0) eax = value;
                else if (register == 1) ecx = value;
                else throw new NotSupportedException("Pure getter mutates an unowned register.");
            }
            CallSiteSymbol MemoryAddress(InterfaceInstruction row)
            {
                if (row.Sib != 0 || (row.ModRm & 7) is not (0 or 1) ||
                    Register(row.ModRm & 7) is not { PointerRead: false } value)
                    throw new NotSupportedException("Getter memory operand has no single receiver-field authority.");
                return new(false, checked(value.Offset + row.Displacement));
            }
            for (var count = 0; count < 64 && cursor - start < 256; ++count)
            {
                var row = InterfaceDecode(code, cursor, previous);
                if (row.At != cursor || code[row.At] != row.Opcode || row.Next - start > 256)
                    throw new NotSupportedException("Getter has an unowned instruction prefix/extent.");
                for (var index = row.At; index < row.Next; ++index)
                    if (!executable(checked(codeBase + (uint)index)))
                        throw new InvalidDataException("Getter instruction leaves its decoded source executable extent.");
                var mode = row.ModRm >> 6; var register = row.ModRm >> 3 & 7;
                if (row.Opcode == 0x90) { }
                else if (row.Opcode == 0x55)
                {
                    if (savedFrame || frame || count != 0) throw new NotSupportedException("Getter frame allocation is unowned.");
                    savedFrame = true;
                }
                else if (row.Opcode is 0x8b or 0x89 && mode == 3)
                {
                    var destination = row.Opcode == 0x8b ? register : row.ModRm & 7;
                    var source = row.Opcode == 0x8b ? row.ModRm & 7 : register;
                    if (destination == 5 && source == 4 && savedFrame && !frame)
                        frame = true;
                    else if (destination == 4 && source == 5 && frame && !frameRetiring)
                    { frameRetiring = true; localWords = 0; locals.Clear(); }
                    else RegisterWrite(destination, Register(source));
                }
                else if (row.Opcode == 0x51)
                {
                    if (!frame || frameRetiring) throw new NotSupportedException("Getter has an unowned stack allocation.");
                    ++localWords; locals.Add(checked(-4 * localWords), ecx);
                }
                else if (row.Opcode is 0x89 or 0x8b && mode != 3 && (row.ModRm & 7) == 5 && row.Sib == 0)
                {
                    if (!frame || frameRetiring || !locals.ContainsKey(row.Displacement))
                        throw new NotSupportedException("Getter stack operand is not an allocated receiver local.");
                    if (row.Opcode == 0x89) locals[row.Displacement] = Register(register);
                    else RegisterWrite(register, locals[row.Displacement]);
                }
                else if (row.Opcode == 0x8b && mode != 3)
                {
                    var memory = MemoryAddress(row); RegisterWrite(register, new(true, memory.Offset));
                }
                else if (row.Opcode == 0x8d && mode != 3)
                    RegisterWrite(register, MemoryAddress(row));
                else if (row.Opcode is 0x81 or 0x83 && row.ModRm == 0xc1 && ecx is { PointerRead: false } offsetReceiver)
                    ecx = new(false, checked(offsetReceiver.Offset + (row.Immediate ?? throw new InvalidDataException("Getter offset is absent."))));
                else if (row.Opcode == 0xe8 && ecx is { PointerRead: false } callReceiver)
                {
                    var target = unchecked(codeBase + (uint)row.Next + (uint)(row.Immediate ?? throw new InvalidDataException("Getter call target is absent.")));
                    var child = ReadEnginePointerGetter(code, codeBase, target, executable, active, depth + 1);
                    children.Add(child); eax = new(true, checked(callReceiver.Offset + child.MemberOffset)); ecx = null;
                }
                else if (row.Opcode == 0x5d)
                {
                    if (!savedFrame || !frame || !frameRetiring || localWords != 0)
                        throw new NotSupportedException("Getter does not retire its genuine stack frame.");
                    savedFrame = frame = frameRetiring = false;
                }
                else if (row.Opcode == 0xc3)
                {
                    if (savedFrame || frame || localWords != 0 || eax is not { PointerRead: true, Offset: >= 0 } result)
                        throw new NotSupportedException("Getter has no complete zero-argument thiscall pointer result.");
                    var extent = row.Next - start;
                    return new(entry, result.Offset, extent,
                        Convert.ToHexString(SHA256.HashData(code.AsSpan(start, extent))), children.ToArray());
                }
                else throw new NotSupportedException("Source call target requires its other memory, branch, argument or effect owner.");
                previous = row; cursor = row.Next;
            }
            throw new NotSupportedException("Pointer getter exceeds its explicit complete-source inspection budget.");
        }
        finally { active.Remove(entry); }
    }

    private static int CallSiteOffset(uint address, uint codeBase, int extent, int bytes)
    {
        if (address < codeBase || (ulong)address - codeBase + (uint)bytes > (ulong)extent)
            throw new InvalidDataException("Call/getter source has no complete decoded instruction extent.");
        return checked((int)(address - codeBase));
    }
}
