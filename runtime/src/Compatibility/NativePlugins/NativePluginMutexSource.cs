using System.Buffers.Binary;
using System.ComponentModel;
using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Names are exact borrowed declarations from the selected, readonly original
// image. Heap/configuration-generated names need their own genuine producer.
internal sealed class NativePluginMutexSource : IDisposable
{
    internal static readonly IReadOnlySet<string> Apis = new HashSet<string>(StringComparer.Ordinal) {
        "CreateMutexA", "CreateMutexW", "CreateMutexExA", "CreateMutexExW", "OpenMutexA", "OpenMutexW",
        "ReleaseMutex", "WaitForSingleObject", "WaitForSingleObjectEx", "WaitForMultipleObjects",
        "WaitForMultipleObjectsEx", "CloseHandle" };
    private readonly FileStream _source;
    private readonly PEReader _pe;
    private readonly HashSet<string> _imports;
    private readonly NativeNvsePlugin _plugin;
    internal NativePluginMutexSource(NativeNvsePlugin plugin)
    {
        _plugin = plugin;
        _source = NativeNvseHostSource.Lease(plugin.Path, plugin.Sha256, dll: true);
        try
        {
            _pe = new PEReader(_source, PEStreamOptions.LeaveOpen);
            var declaration = NativePluginImageDeclarations.Read(plugin.Path, dll: true);
            if (!StringComparer.OrdinalIgnoreCase.Equals(declaration.Sha256, plugin.Sha256))
                throw new InvalidDataException("Mutex source changed from the selected original module.");
            _imports = declaration.Imports.Where(import => import.Name is not null && IsKernel(import.Library))
                .Select(import => import.Name!).ToHashSet(StringComparer.Ordinal);
        }
        catch { _pe?.Dispose(); _source.Dispose(); throw; }
    }
    private static bool IsKernel(string library) => library.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase) ||
        library.Equals("kernelbase.dll", StringComparison.OrdinalIgnoreCase) || library.StartsWith("api-ms-win-core-", StringComparison.OrdinalIgnoreCase);
    internal void RequireApi(NativePluginMutexApi api)
    {
        var name = ApiName(api);
        if (!Apis.Contains(name) || !_imports.Contains(name))
            throw new NotSupportedException("Mutex call has no exact selected original import declaration: " + name);
    }
    internal NativePluginMutexName? Name(uint address, byte[] bytes, uint codePage, byte[] wideBytes, bool wide)
    {
        ObjectDisposedException.ThrowIf(!_source.CanRead, this);
        if (address == 0)
        {
            if (bytes.Length != 0 || wideBytes.Length != 0 || codePage != 0)
                throw new InvalidDataException("Null mutex name was replaced with a declaration.");
            return null;
        }
        var unit = wide ? 2 : 1;
        if (address < _plugin.Image || bytes.Length < unit || bytes.Length > 520 || bytes.Length % unit != 0 ||
            bytes.AsSpan(bytes.Length - unit).IndexOfAnyExcept((byte)0) >= 0 || wide && codePage != 0 || !wide && codePage == 0)
            throw new InvalidDataException("Mutex name has no complete source string/ABI extent.");
        var rva = address - _plugin.Image;
        var section = _pe.PEHeaders.SectionHeaders.FirstOrDefault(header => rva >= header.VirtualAddress &&
            (ulong)rva + (uint)bytes.Length <= (ulong)header.VirtualAddress + (uint)header.SizeOfRawData);
        if (section.SizeOfRawData == 0 || (section.SectionCharacteristics & SectionCharacteristics.MemRead) == 0 ||
            (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0)
            throw new NotSupportedException("Mutex name has no readonly file-backed original declaration owner.");
        var stored = new byte[bytes.Length]; _source.Position = checked(section.PointerToRawData + rva - section.VirtualAddress);
        _source.ReadExactly(stored);
        if (!stored.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Borrowed mutex name differs from unchanged original bytes.");
        var name = wide ? DecodeWide(bytes) : ConvertAnsi(bytes, codePage);
        if (!EncodeWide(name).AsSpan().SequenceEqual(wideBytes))
            throw new InvalidDataException("Mutex caller's actual Windows name conversion disagrees with the source declaration.");
        if (name.Length >= 260) throw new NotSupportedException("Mutex name exceeds the public Windows named-object extent.");
        var tail = name.StartsWith("Global\\", StringComparison.Ordinal) ? name[7..] :
            name.StartsWith("Local\\", StringComparison.Ordinal) ? name[6..] : name;
        if (tail.Contains('\\')) throw new NotSupportedException("Mutex private namespace has no retained namespace/security owner.");
        return new(address, rva, _plugin.Sha256, bytes.ToImmutableArray(), name, wide, codePage);
    }
    internal static string DecodeWide(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes.Length % 2 != 0 || bytes[^1] != 0 || bytes[^2] != 0)
            throw new InvalidDataException("Mutex wide declaration is not completely terminated.");
        var chars = new char[bytes.Length / 2 - 1];
        for (var at = 0; at < chars.Length; ++at)
        {
            chars[at] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at * 2));
            if (chars[at] == 0) throw new InvalidDataException("Mutex wide declaration includes an earlier terminator.");
        }
        return new(chars);
    }
    internal static byte[] EncodeWide(string value)
    {
        var bytes = new byte[checked((value.Length + 1) * 2)];
        for (var at = 0; at < value.Length; ++at) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at * 2), value[at]);
        return bytes;
    }
    private static string ConvertAnsi(byte[] bytes, uint codePage)
    {
        if (bytes[^1] != 0 || bytes.AsSpan(0, bytes.Length - 1).IndexOf((byte)0) >= 0)
            throw new InvalidDataException("Mutex ANSI declaration includes an earlier terminator.");
        // This calls the public Windows converter using the caller's observed
        // code page. It does not replace the original CreateMutexA call.
        var count = ConvertMutexAnsi(codePage, 0, bytes, bytes.Length, null, 0);
        if (count == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var chars = new char[count];
        if (ConvertMutexAnsi(codePage, 0, bytes, bytes.Length, chars, count) != count || chars[^1] != 0)
            throw new InvalidDataException("Actual Windows ANSI mutex conversion did not retain its terminated extent.");
        return new(chars, 0, count - 1);
    }
    internal static string ApiName(NativePluginMutexApi api) => api switch
    {
        NativePluginMutexApi.CreateA => "CreateMutexA",
        NativePluginMutexApi.CreateW => "CreateMutexW",
        NativePluginMutexApi.CreateExA => "CreateMutexExA",
        NativePluginMutexApi.CreateExW => "CreateMutexExW",
        NativePluginMutexApi.OpenA => "OpenMutexA",
        NativePluginMutexApi.OpenW => "OpenMutexW",
        NativePluginMutexApi.Release => "ReleaseMutex",
        NativePluginMutexApi.Close => "CloseHandle",
        NativePluginMutexApi.Wait => "WaitForSingleObject",
        NativePluginMutexApi.WaitEx => "WaitForSingleObjectEx",
        NativePluginMutexApi.WaitMany => "WaitForMultipleObjects",
        NativePluginMutexApi.WaitManyEx => "WaitForMultipleObjectsEx",
        _ => throw new InvalidDataException("Unknown mutex API.")
    };
    public void Dispose() { _pe.Dispose(); _source.Dispose(); }
    [DllImport("kernel32", EntryPoint = "MultiByteToWideChar", SetLastError = true)]
    private static extern int ConvertMutexAnsi(uint codePage, uint flags, byte[] input, int bytes,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[]? output, int chars);
}
