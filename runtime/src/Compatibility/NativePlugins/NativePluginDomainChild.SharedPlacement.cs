using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    internal NativePluginSharedMemoryObservation? ObserveSharedAddress(ulong address, bool permitAddressLimit = false)
    {
        RequireSharedProcess();
        if (address >= 1UL << 32) throw new ArgumentOutOfRangeException(nameof(address));
        if (QueryOriginal(CngCreationHandle, (nint)(nuint)address, out var value,
            (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (permitAddressLimit && error == 87) return null;
            throw new Win32Exception(error, "Actual shared-placement address observation failed.");
        }
        var result = new NativePluginSharedMemoryObservation((ulong)(nuint)value.Base,
            (ulong)(nuint)value.AllocationBase, (ulong)value.Region, value.State, value.Protection, value.Type, value.AllocationProtection);
        if (result.Extent == 0 || result.Base > address || result.End <= address)
            throw new InvalidDataException("Actual process address query did not cover the requested address.");
        return result;
    }
    internal (uint Address, uint Error) ReserveSharedAddress(uint address, uint extent)
    {
        RequireSharedProcess();
        var value = ReservePlaceholder(CngCreationHandle, (nint)(nuint)address, extent, 0x42000, 1, 0, 0);
        var error = value == 0 ? unchecked((uint)Marshal.GetLastWin32Error()) : 0;
        return (NarrowSharedAddress(value), error);
    }
    internal (uint Address, uint Error) MapSharedAddress(NativePluginCngSectionHandle section,
        uint address, uint extent, ulong offset)
    {
        RequireSharedProcess();
        if (section.IsClosed || section.IsInvalid) throw new InvalidOperationException("Shared placement lost its retained actual section.");
        var value = ReplacePlaceholder(section, CngCreationHandle, (nint)(nuint)address,
            offset, extent, 0x4000, 4, 0, 0);
        var error = value == 0 ? unchecked((uint)Marshal.GetLastWin32Error()) : 0;
        return (NarrowSharedAddress(value), error);
    }
    internal (bool Success, uint Error) FreeSharedPlaceholder(uint address)
    {
        RequireSharedProcess();
        var success = ReleasePlaceholder(CngCreationHandle, (nint)(nuint)address, 0, 0x8000);
        return (success, success ? 0 : unchecked((uint)Marshal.GetLastWin32Error()));
    }
    internal (bool Success, uint Error) UnmapSharedAddress(uint address)
    {
        RequireSharedProcess();
        var success = ReleaseSharedView(CngCreationHandle, (nint)(nuint)address, 0);
        return (success, success ? 0 : unchecked((uint)Marshal.GetLastWin32Error()));
    }
    internal (bool Success, uint Error, uint Previous) QuarantineSharedAddress(uint address, uint extent)
    {
        RequireSharedProcess();
        var success = QuarantineSharedView(CngCreationHandle, (nint)(nuint)address, extent, 1, out var previous);
        return (success, success ? 0 : unchecked((uint)Marshal.GetLastWin32Error()), previous);
    }
    internal static (uint Page, uint Granularity, ulong Minimum) SharedSystemGranularity()
    {
        GetSharedSystemInfo(out var value);
        if (value.Page == 0 || value.Granularity == 0 || (value.Page & (value.Page - 1)) != 0 ||
            value.Granularity % value.Page != 0 || value.Minimum == 0)
            throw new InvalidDataException("Actual Windows page/allocation granularity is invalid.");
        return (value.Page, value.Granularity, (ulong)(nuint)value.Minimum);
    }
    private void RequireSharedProcess()
    {
        if (ResourcesRetired || HasExited)
            throw new InvalidOperationException("Shared placement requires its exact living process creation handle.");
    }
    private static uint NarrowSharedAddress(nint value)
    {
        var address = (ulong)(nuint)value;
        if (address > uint.MaxValue)
            throw new InvalidDataException("Actual x86 allocation returned a non-x86 address; the retained operation cannot replay.");
        return checked((uint)address);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SharedSystemInfo
    {
        internal uint Architecture, Page;
        internal nint Minimum, Maximum;
        internal nuint Mask;
        internal uint Processors, ProcessorType, Granularity;
        internal ushort Level, Revision;
    }
    [DllImport("kernel32", EntryPoint = "GetSystemInfo")]
    private static extern void GetSharedSystemInfo(out SharedSystemInfo information);
    [DllImport("kernelbase", EntryPoint = "VirtualAlloc2", SetLastError = true)]
    private static extern nint ReservePlaceholder(SafeHandle process, nint address, nuint size,
        uint allocation, uint protection, nint parameters, uint parameterCount);
    [DllImport("kernelbase", EntryPoint = "MapViewOfFile3", SetLastError = true)]
    private static extern nint ReplacePlaceholder(SafeHandle section, SafeHandle process, nint address,
        ulong offset, nuint size, uint allocation, uint protection, nint parameters, uint parameterCount);
    [DllImport("kernel32", EntryPoint = "VirtualFreeEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleasePlaceholder(SafeHandle process, nint address, nuint size, uint type);
    [DllImport("kernelbase", EntryPoint = "UnmapViewOfFile2", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseSharedView(SafeHandle process, nint address, uint flags);
    [DllImport("kernel32", EntryPoint = "VirtualProtectEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QuarantineSharedView(SafeHandle process, nint address, nuint size,
        uint protection, out uint previous);
}
