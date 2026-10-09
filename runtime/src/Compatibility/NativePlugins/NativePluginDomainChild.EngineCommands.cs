using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    internal void RequireEngineCommandCallable(uint source, uint target, bool retired)
    {
        if (ResourcesRetired || HasExited || source == 0 || target == 0 || source > uint.MaxValue - 7)
            throw new InvalidOperationException("Engine callable readback lost its actual living creation-process owner.");
        if (QueryOriginal(CngCreationHandle, (nint)(nuint)source, out var actual, (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (actual.State != 0x1000 || actual.Type != 0x20000 || actual.Protection != 0x20 ||
            (nuint)actual.Base > source || (ulong)(nuint)actual.Base + (ulong)actual.Region < (ulong)source + 7)
            throw new InvalidDataException("Engine callable does not retain its actual first-party executable private page.");
        var observed = new byte[7];
        if (!ReadEngineMemory(CngCreationHandle, (nint)(nuint)source, observed, 7, out var read) || read != 7)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Engine callable publication has no complete native readback.");
        if (retired)
        {
            if (observed.Any(value => value != 0xcc)) throw new InvalidDataException("Retired engine callable did not become its actual reserved trap extent.");
            return;
        }
        if (observed[0] != 0xb8 || observed[5] != 0xff || observed[6] != 0xe0 || BinaryPrimitives.ReadUInt32LittleEndian(observed.AsSpan(1)) != target)
            throw new InvalidDataException("Engine callable bytes differ from the first-party jump publication.");
        if (QueryOriginal(CngCreationHandle, (nint)(nuint)target, out var code, (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (code.State != 0x1000 || code.Type != 0x1000000 || code.Protection is not (0x10 or 0x20 or 0x40 or 0x80))
            throw new InvalidDataException("Engine callable target is not current executable first-party image code.");
        var mapped = new StringBuilder(32768); var creation = new StringBuilder(32768); uint capacity = 32768;
        var mappedCount = MappedEngineImage(CngCreationHandle, (nint)(nuint)target, mapped, 32768);
        if (mappedCount == 0 ||
            !CreationEngineImage(CngCreationHandle, 1, creation, ref capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (mappedCount >= 32768 || capacity >= 32768)
            throw new InvalidDataException("Engine callable image identity is truncated.");
        if (!StringComparer.OrdinalIgnoreCase.Equals(mapped.ToString(), creation.ToString()))
            throw new InvalidDataException("Engine callable target does not belong to the exact actual companion process image.");
    }
    [DllImport("kernel32", EntryPoint = "ReadProcessMemory", SetLastError = true)]
    private static extern bool ReadEngineMemory(SafeHandle process, nint address, byte[] bytes, nuint count, out nuint read);
    [DllImport("psapi", EntryPoint = "GetMappedFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint MappedEngineImage(SafeHandle process, nint address, StringBuilder path, uint capacity);
    [DllImport("kernel32", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreationEngineImage(SafeHandle process, uint flags, StringBuilder path, ref uint capacity);
}
