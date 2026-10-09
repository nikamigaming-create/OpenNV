using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    private static void AppendSourceAddressSpaceArguments(System.Diagnostics.ProcessStartInfo start,
        NativePluginSourceAddressSpace? source)
    {
        if (source is null) return;
        start.ArgumentList.Add("--source-image");
        start.ArgumentList.Add(source.ImageBase.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(source.ImageBytes.ToString(CultureInfo.InvariantCulture));
    }

    private static void ReserveSourceAddressSpace(NativePluginKernelHandle process, NativePluginSourceAddressSpace source)
    {
        SourceAddressSystemInfo(out var system);
        var granularity = system.AllocationGranularity;
        if (granularity == 0 || (granularity & (granularity - 1)) != 0 || source.ImageBase % granularity != 0)
            throw new InvalidDataException("Source image address identity has no actual Windows allocation alignment.");
        var bytes = checked(((ulong)source.ImageBytes + granularity - 1) / granularity * granularity);
        if (bytes == 0 || (ulong)source.ImageBase + bytes > 1UL << 32)
            throw new InvalidDataException("Source image address reservation leaves its complete x86 extent.");
        var requested = (nint)(nuint)source.ImageBase;
        // This runs while the exact newly-created primary thread is suspended,
        // before its CRT, provider, DLL, heap or table allocations can claim it.
        var actual = ReserveOriginalSourceImage(process, requested, checked((nuint)bytes), 0x2000, 1);
        if (actual != requested)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Selected source image addresses could not reserve before native startup.");
        if (QueryOriginal(process, requested, out var memory, (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (memory.Base != requested || memory.AllocationBase != requested || (ulong)memory.Region != bytes ||
            memory.State != 0x2000 || memory.Type != 0x20000 || memory.Protection != 0 || memory.AllocationProtection != 1)
            throw new InvalidDataException("Source image reservation differs from its actual no-access creation-process lifetime.");
        // Any construction failure is retired with the existing retained exact
        // child handle. Never release it into a running failed generation.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SourceAddressSystemInformation
    {
        internal uint ArchitectureAndReserved, PageSize;
        internal nint MinimumAddress, MaximumAddress;
        internal nuint ProcessorMask;
        internal uint Processors, ProcessorType, AllocationGranularity;
        internal ushort ProcessorLevel, ProcessorRevision;
    }
    [DllImport("kernel32", EntryPoint = "GetSystemInfo")]
    private static extern void SourceAddressSystemInfo(out SourceAddressSystemInformation system);
    [DllImport("kernel32", EntryPoint = "VirtualAllocEx", SetLastError = true)]
    private static extern nint ReserveOriginalSourceImage(SafeHandle process, nint address, nuint bytes, uint allocation, uint protection);
}
