using System.Runtime.InteropServices;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    // This observes only the creation handle already retained by this owner.
    // It cannot publish a callable, change protection, or open another process.
    internal object CaptureCallableCollision(uint address)
    {
        if (ResourcesRetired || HasExited || address == 0)
            throw new InvalidOperationException("Callable collision observation lost its living creation-process owner.");
        var query = QueryOriginal(CngCreationHandle, (nint)(nuint)address, out var memory,
            (nuint)Marshal.SizeOf<CngMemoryInformation>());
        var queryError = query == 0 ? Marshal.GetLastWin32Error() : 0;
        var path = new StringBuilder(32768);
        var copied = MappedEngineImage(CngCreationHandle, (nint)(nuint)address, path, (uint)path.Capacity);
        var pathError = copied == 0 || copied >= path.Capacity ? Marshal.GetLastWin32Error() : 0;
        return new
        {
            Address = address,
            QueryBytes = (ulong)query,
            QueryError = queryError,
            Base = (ulong)(nuint)memory.Base,
            AllocationBase = (ulong)(nuint)memory.AllocationBase,
            RegionBytes = (ulong)memory.Region,
            memory.State,
            memory.Type,
            memory.Protection,
            memory.AllocationProtection,
            MappedFile = copied == 0 ? null : path.ToString(),
            MappedFileCharacters = copied,
            MappedFileError = pathError,
            MappedFileTruncated = copied >= path.Capacity
        };
    }
}
