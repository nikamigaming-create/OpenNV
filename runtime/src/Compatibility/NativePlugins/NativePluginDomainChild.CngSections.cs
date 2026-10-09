using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    private SafeHandle CngCreationHandle => _process is { } process ? process :
        _regular?.SafeHandle ?? throw new InvalidOperationException("CNG section transfer lacks the retained actual creation handle.");

    internal NativePluginCngSectionHandle RetainCngSection(uint original)
    {
        if (ResourcesRetired || HasExited || original is 0 or uint.MaxValue)
            throw new InvalidOperationException("CNG section transfer requires its actual live original process/handle.");
        if (!DuplicateFromOriginal(CngCreationHandle, (nint)(nuint)original, CurrentProcess(), out var retained, 6, false, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual original section could not retain a read/write parent reference.");
        return new(retained);
    }
    internal uint ReceiveCngSection(NativePluginCngSectionHandle retained)
    {
        if (ResourcesRetired || HasExited || retained.IsClosed || retained.IsInvalid)
            throw new InvalidOperationException("CNG section target lost its actual process or retained parent section.");
        if (!DuplicateToService(CurrentProcess(), retained, CngCreationHandle, out var remote, 6, false, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual CNG service did not receive its duplicated section reference.");
        var value = unchecked((ulong)remote);
        if (value > uint.MaxValue && (value >> 32) != uint.MaxValue)
            throw new InvalidDataException("Duplicated CNG handle has no actual x86 handle extent.");
        var narrowed = unchecked((uint)value);
        if (narrowed is 0 or uint.MaxValue) throw new InvalidDataException("Duplicated CNG handle is null or a pseudo handle.");
        return narrowed;
    }
    internal void RequireCngMappedView(NativePluginCngSharedSource source)
    {
        if (ResourcesRetired || HasExited) throw new InvalidOperationException("CNG source process is no longer alive.");
        if (QueryOriginal(CngCreationHandle, (nint)(nuint)source.Address, out var memory, (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((nuint)memory.Base != source.Address || memory.AllocationBase != memory.Base || memory.State != 0x1000 ||
            memory.Type != 0x40000 || memory.Protection != 4 || memory.Region < source.Length ||
            source.Kind == 1 && memory.Region != source.Maximum)
            throw new InvalidDataException("Actual original shared view does not agree with its retained source extent/protection.");
    }
    internal static void RequireSameCngSection(NativePluginCngSectionHandle first, NativePluginCngSectionHandle second)
    {
        if (!CompareSections(first, second))
            throw new InvalidDataException("Two declared original aliases do not retain the same actual Windows section object.",
                new Win32Exception(Marshal.GetLastWin32Error()));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CngMemoryInformation
    {
        internal nint Base, AllocationBase;
        internal uint AllocationProtection;
        internal ushort Partition;
        internal nuint Region;
        internal uint State, Protection, Type;
    }
    [DllImport("kernel32", EntryPoint = "GetCurrentProcess")] private static extern nint CurrentProcess();
    [DllImport("kernel32", EntryPoint = "DuplicateHandle", SetLastError = true)]
    private static extern bool DuplicateFromOriginal(SafeHandle sourceProcess, nint source, nint targetProcess,
        out nint target, uint access, bool inherit, uint options);
    [DllImport("kernel32", EntryPoint = "DuplicateHandle", SetLastError = true)]
    private static extern bool DuplicateToService(nint sourceProcess, SafeHandle source, SafeHandle targetProcess,
        out nint target, uint access, bool inherit, uint options);
    [DllImport("kernel32", EntryPoint = "VirtualQueryEx", SetLastError = true)]
    private static extern nuint QueryOriginal(SafeHandle process, nint address, out CngMemoryInformation memory, nuint size);
    [DllImport("kernelbase", EntryPoint = "CompareObjectHandles", SetLastError = true)]
    private static extern bool CompareSections(SafeHandle first, SafeHandle second);
}
