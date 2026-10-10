using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeEngineTlsCarrierBuild(string Path, string Sha256, FileStream Lease, uint IndexRva);

internal static class NativePluginEngineTlsBuild
{
    internal static NativeEngineTlsCarrierBuild Read(string companion)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(companion))!;
        using var manifest = new FileStream(System.IO.Path.Combine(directory, "build-manifest.json"),
            FileMode.Open, FileAccess.Read, FileShare.Read);
        using var json = JsonDocument.Parse(manifest); var root = json.RootElement;
        if (!root.GetProperty("authoredOnly").GetBoolean() || root.GetProperty("machine").GetString() != "I386")
            throw new InvalidDataException("Static TLS carrier has no actual first-party x86 build-manifest owner.");
        var companionRows = root.GetProperty("files").EnumerateArray().Where(row =>
            row.GetProperty("path").GetString() == "opennv_plugin_domain.exe").ToArray();
        if (companionRows.Length != 1 || !StringComparer.OrdinalIgnoreCase.Equals(System.IO.Path.GetFullPath(companion),
            System.IO.Path.Combine(directory, "opennv_plugin_domain.exe")))
            throw new InvalidDataException("Static TLS build does not identify the actual selected first-party companion.");
        using (var executable = NativeNvseHostSource.Lease(companion,
            companionRows[0].GetProperty("sha256").GetString() ?? throw new InvalidDataException("Companion build hash is absent."), dll: false))
            if ((ulong)executable.Length != companionRows[0].GetProperty("bytes").GetUInt64())
                throw new InvalidDataException("Static TLS companion differs from its declared authored build extent.");
        var rows = root.GetProperty("files").EnumerateArray().Where(row =>
            row.GetProperty("path").GetString() == "opennv_engine_tls.dll").ToArray();
        if (rows.Length != 1) throw new InvalidDataException("Static TLS carrier is absent or duplicated in its retained first-party build.");
        var hash = rows[0].GetProperty("sha256").GetString() ?? throw new InvalidDataException("Static TLS carrier hash is absent.");
        var path = System.IO.Path.Combine(directory, "opennv_engine_tls.dll");
        var lease = NativeNvseHostSource.Lease(path, hash, dll: true);
        try
        {
            if ((ulong)lease.Length != rows[0].GetProperty("bytes").GetUInt64())
                throw new InvalidDataException("Static TLS carrier byte extent changed after its authored build.");
            using var pe = new PEReader(lease, PEStreamOptions.LeaveOpen);
            var h = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Static TLS carrier has no PE32 header.");
            if (h.Magic != PEMagic.PE32 || pe.PEHeaders.CoffHeader.Machine != Machine.I386 ||
                h.AddressOfEntryPoint != 0 || h.ImportTableDirectory.Size != 0 || h.DelayImportTableDirectory.Size != 0 ||
                h.ThreadLocalStorageTableDirectory.RelativeVirtualAddress == 0 || h.ThreadLocalStorageTableDirectory.Size != 24)
                throw new InvalidDataException("First-party carrier is not the declared entry-free/import-free compiler TLS owner.");
            var directoryBytes = pe.GetSectionData(h.ThreadLocalStorageTableDirectory.RelativeVirtualAddress).GetContent(0, 24);
            var index = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(directoryBytes.AsSpan().Slice(8));
            if (index < h.ImageBase || (ulong)index - h.ImageBase + 4 > (ulong)h.SizeOfImage)
                throw new InvalidDataException("Carrier's actual loader index is outside its own declared image.");
            var callbacks = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(directoryBytes.AsSpan().Slice(12));
            if (callbacks != 0 && (callbacks < h.ImageBase || (ulong)callbacks - h.ImageBase + 4 > (ulong)h.SizeOfImage ||
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                    pe.GetSectionData(checked((int)(callbacks - h.ImageBase))).GetContent(0, 4).AsSpan()) != 0))
                throw new InvalidDataException("Authored static TLS carrier has an unowned callback/constructor body.");
            lease.Position = 0;
            return new(path, hash, lease, checked((uint)(index - h.ImageBase)));
        }
        catch { lease.Dispose(); throw; }
    }
}
