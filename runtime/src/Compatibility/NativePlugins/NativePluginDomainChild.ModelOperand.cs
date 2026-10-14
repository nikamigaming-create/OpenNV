using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    internal byte[] ReadSourceModelOperand(uint instruction, uint imageBase, uint imageBytes, uint protection)
        => ReadModelExtent(instruction, 5, imageBase, imageBytes, 0x20000, protection);
    internal byte[] ReadModuleModelBuffer(uint image, uint imageBytes, uint rva, uint capacity)
        => ReadModelExtent(checked(image + rva), checked((int)capacity), image, imageBytes, 0x1000000, null);
    internal byte[] ReadModuleModelWriter(uint image, uint imageBytes, uint rva)
        => ReadModelExtent(checked(image + rva), 47, image, imageBytes, 0x1000000, null, executable: true);
    private byte[] ReadModelExtent(uint address, int count, uint imageBase, uint imageBytes, uint type, uint? protection, bool executable = false)
    {
        if (ResourcesRetired || HasExited || count <= 0 || count > NativePluginExecutionDomain.MaximumPayload ||
            address < imageBase || (ulong)address + (uint)count > (ulong)imageBase + imageBytes)
            throw new InvalidOperationException("Model input readback lost its actual creation-process/image extent.");
        var end = (ulong)address + (uint)count;
        for (var cursor = (ulong)address; cursor < end;)
        {
            if (QueryOriginal(CngCreationHandle, (nint)(nuint)cursor, out var region, (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Model input native region query failed.");
            var next = (ulong)(nuint)region.Base + (ulong)region.Region;
            if ((nuint)region.AllocationBase != imageBase || region.State != 0x1000 || region.Type != type ||
                (protection is { } access ? region.Protection != access : executable ?
                    region.Protection is not (0x10 or 0x20 or 0x40 or 0x80) : region.Protection is not (0x02 or 0x04 or 0x08)) ||
                (ulong)(nuint)region.Base > cursor || next <= cursor)
                throw new InvalidDataException("Model input changed its selected-image/module data mapping or protection.");
            cursor = Math.Min(next, end);
        }
        var bytes = new byte[count];
        if (!ReadEngineMemory(CngCreationHandle, (nint)(nuint)address, bytes, (nuint)count, out var read) || read != (nuint)count)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Model input has no complete native byte readback.");
        return bytes;
    }
}
