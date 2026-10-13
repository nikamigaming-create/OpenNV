using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    internal NativeSourceCallSiteCode ReadSourceCallSite(uint source, uint imageBase, uint imageBytes,
        uint defaultTarget, uint originalImage, uint protection, bool getter, bool retired = false, bool validateTarget = true)
    {
        var extent = getter ? 6 : 5;
        if (ResourcesRetired || HasExited || source < imageBase || imageBytes == 0 ||
            (ulong)source + (uint)extent > (ulong)imageBase + imageBytes || getter && defaultTarget == 0)
            throw new InvalidOperationException("Source CALL readback lost its exact creation-process/image lifetime.");
        var end = (ulong)source + (uint)extent; var cursor = (ulong)source;
        while (cursor < end)
        {
            if (QueryOriginal(CngCreationHandle, (nint)(nuint)cursor, out var region,
                (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Source CALL native region query failed.");
            var next = (ulong)(nuint)region.Base + (ulong)region.Region;
            if ((nuint)region.AllocationBase != imageBase || region.State != 0x1000 || region.Type != 0x20000 ||
                region.Protection != protection || (ulong)(nuint)region.Base > cursor || next <= cursor)
                throw new InvalidDataException("Source CALL does not retain its selected first-party committed page/protection.");
            cursor = Math.Min(next, end);
        }
        var bytes = new byte[extent];
        if (!ReadEngineMemory(CngCreationHandle, (nint)(nuint)source, bytes, (nuint)extent, out var read) || read != (nuint)extent)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Source CALL native bytes have no complete readback.");
        var observation = NativeSourceCallSiteCode.Read(source, bytes, getter, retired);
        if (observation.Kind != NativeSourceCallSiteCodeKind.Call || !validateTarget) return observation;
        var target = observation.Target;
        if (QueryOriginal(CngCreationHandle, (nint)(nuint)target, out var code,
            (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Source CALL target query failed.");
        if (code.State != 0x1000 || code.Type != 0x1000000 || code.Protection is not (0x10 or 0x20 or 0x40 or 0x80))
            throw new InvalidDataException("Source CALL target is not an actual executable retained image.");
        if (target != defaultTarget)
        {
            if ((nuint)code.AllocationBase != originalImage)
                throw new NotSupportedException("Installed source hook leaves its actual original module image.");
            return observation;
        }
        var mapped = new StringBuilder(32768); var creation = new StringBuilder(32768); uint capacity = 32768;
        var count = MappedEngineImage(CngCreationHandle, (nint)(nuint)target, mapped, 32768);
        if (count == 0 || !CreationEngineImage(CngCreationHandle, 1, creation, ref capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Source CALL first-party target identity query failed.");
        if (count >= 32768 || capacity >= 32768 || !StringComparer.OrdinalIgnoreCase.Equals(mapped.ToString(), creation.ToString()))
            throw new InvalidDataException("Source CALL getter is outside the exact living first-party companion image.");
        return observation;
    }

    internal uint ReadSourceCallSiteMember(uint receiver, uint offset)
    {
        if (ResourcesRetired || HasExited || receiver == 0 || (ulong)receiver + offset + 4 > 1UL << 32)
            throw new InvalidOperationException("Source getter lost its actual living object/creation-process lease.");
        var address = checked(receiver + offset);
        if (QueryOriginal(CngCreationHandle, (nint)(nuint)address, out var memory,
            (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Native getter member query failed.");
        if (memory.State != 0x1000 || memory.Type != 0x20000 || memory.Protection != 0x02 ||
            (ulong)(nuint)memory.Base > address || (ulong)(nuint)memory.Base + (ulong)memory.Region < (ulong)address + 4)
            throw new InvalidDataException("Native getter member is outside its actual complete read-only object allocation.");
        var bytes = new byte[4];
        if (!ReadEngineMemory(CngCreationHandle, (nint)(nuint)address, bytes, 4, out var read) || read != 4)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Native getter member read has no complete extent.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }
}
