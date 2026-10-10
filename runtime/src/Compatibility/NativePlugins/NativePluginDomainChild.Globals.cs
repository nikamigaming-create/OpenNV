using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    internal void RequireEngineTlsBuildProcess(string companion)
    {
        RequireEngineCreationLifetime(); var name = new StringBuilder(32768); uint capacity = 32768;
        if (!CreationEngineImage(CngCreationHandle, 1, name, ref capacity) || capacity == 0 || capacity >= 32768)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Static TLS build actual creation-image identity query failed.");
        using var actual = new FileStream("\\\\?\\GLOBALROOT" + name, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var declared = new FileStream(companion, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (NativePluginSteamFileIdentity.Read(actual) != NativePluginSteamFileIdentity.Read(declared))
            throw new InvalidDataException("Static TLS build is not beside this exact living first-party companion image.");
    }
    internal uint ReadEngineGlobal(uint address, uint imageBase, uint imageBytes)
    {
        if (address % 4 != 0 || address < imageBase || (ulong)address + 4 > (ulong)imageBase + imageBytes)
            throw new InvalidDataException("Engine global readback leaves its selected source image/cell.");
        RequireEngineRegion(address, 4, 0x20000, 0x02, imageBase);
        return BinaryPrimitives.ReadUInt32LittleEndian(ReadEngineExtent(address, 4));
    }

    internal byte[] ReadEngineStaticTls(uint pointer, uint count)
    {
        if (pointer == 0 || count is 0 or > 65536 || (ulong)pointer + count > 1UL << 32)
            throw new InvalidDataException("Source TLS readback has no complete living x86 extent.");
        RequireEngineRegion(pointer, count, 0x20000, 0x04);
        return ReadEngineExtent(pointer, count);
    }

    internal void RequireEngineTlsCarrier(uint image, uint indexRva, uint index, FileStream file)
    {
        if (image == 0 || (ulong)image + indexRva + 4 > 1UL << 32)
            throw new InvalidDataException("Actual compiler TLS carrier/index leaves its native image.");
        RequireEngineRegion(image + indexRva, 4, 0x1000000, null, image);
        if (BinaryPrimitives.ReadUInt32LittleEndian(ReadEngineExtent(image + indexRva, 4)) != index)
            throw new InvalidDataException("Compiler TLS index differs from its actual Windows-written image cell.");
        var name = new StringBuilder(32768);
        var used = MappedEngineImage(CngCreationHandle, (nint)(nuint)image, name, 32768);
        if (used == 0 || used >= 32768)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual compiler TLS mapped image identity query failed.");
        using var mapped = new FileStream("\\\\?\\GLOBALROOT" + name, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (NativePluginSteamFileIdentity.Read(mapped) != NativePluginSteamFileIdentity.Read(file))
            throw new InvalidDataException("Actual compiler TLS image differs from the retained authored file identity.");
    }

    internal void RequireEngineTlsCarrierGone(uint image)
    {
        RequireEngineCreationLifetime();
        if (QueryOriginal(CngCreationHandle, (nint)(nuint)image, out var memory,
            (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Retired compiler TLS image query failed.");
        if (memory.State != 0x10000)
            throw new InvalidDataException("Compiler TLS carrier still retains its actual Windows image after FreeLibrary.");
    }

    private void RequireEngineRegion(uint pointer, uint extent, uint type, uint? protection, uint? allocation = null)
    {
        RequireEngineCreationLifetime(); var cursor = (ulong)pointer; var end = cursor + extent;
        if (end > 1UL << 32) throw new InvalidDataException("Engine readback exceeds its complete x86 extent.");
        while (cursor < end)
        {
            if (QueryOriginal(CngCreationHandle, (nint)(nuint)cursor, out var region,
                (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Engine native source region query failed.");
            var next = (ulong)(nuint)region.Base + (ulong)region.Region;
            if (region.State != 0x1000 || region.Type != type || protection is { } access && region.Protection != access ||
                allocation is { } basis && (nuint)region.AllocationBase != basis || (ulong)(nuint)region.Base > cursor || next <= cursor ||
                (region.Protection & (0x100U | 0x01U)) != 0)
                throw new InvalidDataException("Engine native source bytes escaped their actual allocation/protection owner.");
            cursor = Math.Min(next, end);
        }
    }
    private byte[] ReadEngineExtent(uint pointer, uint extent)
    {
        RequireEngineCreationLifetime(); var bytes = new byte[checked((int)extent)];
        if (!ReadEngineMemory(CngCreationHandle, (nint)(nuint)pointer, bytes, extent, out var count) || count != extent)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Engine native source readback is incomplete.");
        return bytes;
    }
    private void RequireEngineCreationLifetime()
    {
        if (ResourcesRetired || HasExited)
            throw new InvalidOperationException("Engine source readback lost its exact living creation-process handle.");
    }
}
