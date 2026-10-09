using System.Diagnostics;
using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Version fields are read from the exact owned images, not picked to persuade
// an original plugin's Query. Executable bytes never become runtime authority.
internal sealed class NativeNvseHostSource : IDisposable
{
    private FileStream? _runtimeLease, _nvseLease;
    private ulong? _generation;
    internal string RuntimePath { get; }
    internal string NvsePath { get; }
    internal string RuntimeSha256 { get; }
    internal string NvseSha256 { get; }
    internal string StackIdentity { get; }
    internal string EditionDeclarationOwner { get; }
    internal NativePluginSourceAddressSpace SourceAddressSpace { get; }
    internal uint NvseVersion { get; }
    internal uint RuntimeVersion { get; }
    internal uint NoGore { get; }
    internal string RuntimeDirectory => Path.GetDirectoryName(RuntimePath)! + Path.DirectorySeparatorChar;
    private NativeNvseHostSource(string runtime, string runtimeHash, FileStream runtimeLease,
        string nvse, string nvseHash, FileStream nvseLease, string stack, bool noGore, string editionOwner,
        uint nvseVersion, uint runtimeVersion, NativePluginSourceAddressSpace sourceAddressSpace)
    {
        RuntimePath = runtime; RuntimeSha256 = runtimeHash; _runtimeLease = runtimeLease;
        NvsePath = nvse; NvseSha256 = nvseHash; _nvseLease = nvseLease;
        StackIdentity = stack; NoGore = noGore ? 1U : 0U; EditionDeclarationOwner = editionOwner;
        NvseVersion = nvseVersion; RuntimeVersion = runtimeVersion;
        SourceAddressSpace = sourceAddressSpace;
    }
    internal static NativeNvseHostSource Open(string runtimePath, string runtimeSha256,
        string nvsePath, string nvseSha256, string stackIdentity, bool? sourceNoGore, string editionDeclarationOwner)
    {
        RequireHash(stackIdentity); RequireHash(runtimeSha256); RequireHash(nvseSha256);
        if (sourceNoGore is null || string.IsNullOrWhiteSpace(editionDeclarationOwner))
            throw new NotSupportedException("NVSE runtime edition has no explicit owned-source declaration.");
        var runtime = Path.GetFullPath(runtimePath); var nvse = Path.GetFullPath(nvsePath);
        var installation = NativeGameInstallation.Detect(Path.GetDirectoryName(runtime)!);
        if (installation.Game != NativeGame.FalloutNewVegas)
            throw new NotSupportedException("The selected installation has no New Vegas/xNVSE ABI source owner.");
        FileStream? game = null, dependency = null;
        try
        {
            game = Lease(runtime, runtimeSha256, dll: false); dependency = Lease(nvse, nvseSha256, dll: true);
            var gameVersion = FileVersionInfo.GetVersionInfo(runtime); var nvseVersion = FileVersionInfo.GetVersionInfo(nvse);
            if (gameVersion.FileMajorPart != 1 || nvseVersion.FileMajorPart != 0)
                throw new NotSupportedException("Owned version resources do not establish the public New Vegas/xNVSE encoding.");
            if (gameVersion.FileMinorPart != 4 || gameVersion.FileBuildPart != 0 || gameVersion.FilePrivatePart != 525)
                throw new NotSupportedException("Owned runtime has no reviewed New Vegas 1.4.0.525 target ABI declaration.");
            var packedNvse = Pack(nvseVersion.FileMinorPart, nvseVersion.FileBuildPart, nvseVersion.FilePrivatePart, 0);
            // The published 6.4.8 and 6.4.9 host declarations have the same
            // outer callable layout. Keep the actual selected version: nested
            // interface revisions (notably Data v3/v4) have independent owners.
            if (packedNvse != Pack(6, 4, 8, 0) && packedNvse != Pack(6, 4, 9, 0))
                throw new NotSupportedException("Owned xNVSE version has no reviewed public host interface layout owner.");
            var packedRuntime = Pack(gameVersion.FileMinorPart, gameVersion.FileBuildPart, gameVersion.FilePrivatePart, sourceNoGore.Value ? 1 : 0);
            var result = new NativeNvseHostSource(runtime, runtimeSha256.ToUpperInvariant(), game, nvse,
                nvseSha256.ToUpperInvariant(), dependency, stackIdentity.ToUpperInvariant(), sourceNoGore.Value,
                editionDeclarationOwner, packedNvse, packedRuntime,
                NativePluginSourceAddressSpace.Read(game, runtime, runtimeSha256.ToUpperInvariant()));
            game = null; dependency = null; return result;
        }
        finally { game?.Dispose(); dependency?.Dispose(); }
    }
    internal void Claim(ulong generation)
    {
        ObjectDisposedException.ThrowIf(_runtimeLease is null || _nvseLease is null, this);
        if (_generation is not null) throw new InvalidOperationException("NVSE source leases already belong to a process generation.");
        _generation = generation;
    }
    internal void Check(ulong generation)
    {
        ObjectDisposedException.ThrowIf(_runtimeLease is null || _nvseLease is null, this);
        if (_generation != generation) throw new InvalidOperationException("NVSE source declaration belongs to a different generation.");
    }
    public void Dispose()
    {
        if (_generation is not null) throw new InvalidOperationException("The native generation still owns its NVSE source leases.");
        Close();
    }
    internal void Retire(ulong generation) { Check(generation); _generation = null; Close(); }
    private void Close() { _runtimeLease?.Dispose(); _nvseLease?.Dispose(); _runtimeLease = null; _nvseLease = null; }
    internal static FileStream Lease(string path, string sha256, bool dll)
    {
        RequireHash(sha256);
        var source = LeaseHashed(path, dll, out var actual);
        try
        {
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original native source identity drifted before admission.");
            return source;
        }
        catch (Exception failure)
        {
            try { source.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Original source digest admission and input retirement failed.", failure, cleanup); }
            throw;
        }
    }
    // Explicit first admission of a genuinely discovered source image. This
    // does not turn an absent expected digest into an accepted legacy call.
    internal static FileStream LeaseHashed(string path, bool dll, out string sha256, long? maximumBytes = null)
    {
        var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (maximumBytes is { } bound && (bound < 1 || source.Length < 1 || source.Length > bound))
                throw new InvalidDataException("Original native image exceeds its declared bounded source extent.");
            using (var pe = new PEReader(source, PEStreamOptions.LeaveOpen))
                if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32 ||
                    pe.PEHeaders.CorHeader is not null || pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll) != dll)
                    throw new InvalidDataException("NVSE source admission requires the declared unmanaged Windows PE32/I386 image kind.");
            source.Position = 0;
            sha256 = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
            source.Position = 0; return source;
        }
        catch (Exception failure)
        {
            try { source.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Original source image admission and input retirement failed.", failure, cleanup); }
            throw;
        }
    }
    internal static void RequirePluginExports(FileStream source)
    {
        var position = source.Position;
        try
        {
            source.Position = 0;
            using var pe = new PEReader(source, PEStreamOptions.LeaveOpen);
            RequirePluginExports(pe);
        }
        finally { source.Position = position; }
    }
    private static void RequirePluginExports(PEReader pe)
    {
        var header = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Native plugin export source PE header is absent.");
        if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || header.Magic != PEMagic.PE32)
            throw new InvalidDataException("Native plugin exports require their retained unmanaged PE32/I386 image.");
        var directory = header.ExportTableDirectory;
        if (directory.RelativeVirtualAddress <= 0 || directory.Size < 40)
            throw new InvalidDataException("Original image does not declare an NVSE plugin export directory.");
        var table = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, 40).AsSpan();
        var functions = BinaryPrimitives.ReadUInt32LittleEndian(table[20..]);
        var names = BinaryPrimitives.ReadUInt32LittleEndian(table[24..]);
        var functionRva = BinaryPrimitives.ReadUInt32LittleEndian(table[28..]);
        var namesRva = BinaryPrimitives.ReadUInt32LittleEndian(table[32..]);
        var ordinalsRva = BinaryPrimitives.ReadUInt32LittleEndian(table[36..]);
        if (functions == 0 || names == 0 || names > 1048576 || functions > 1048576 ||
            functionRva == 0 || namesRva == 0 || ordinalsRva == 0)
            throw new InvalidDataException("Original NVSE export directory has invalid bounded table extents.");
        var functionTable = pe.GetSectionData(checked((int)functionRva)).GetContent(0, checked((int)functions * 4));
        var nameTable = pe.GetSectionData(checked((int)namesRva)).GetContent(0, checked((int)names * 4));
        var ordinalTable = pe.GetSectionData(checked((int)ordinalsRva)).GetContent(0, checked((int)names * 2));
        var found = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < names; ++index)
        {
            var nameRva = BinaryPrimitives.ReadUInt32LittleEndian(nameTable.AsSpan().Slice(checked((int)index * 4), 4));
            var bytes = pe.GetSectionData(checked((int)nameRva)).GetContent();
            var end = bytes.AsSpan().IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException("Original export name is not completely terminated.");
            if (end is not (16 or 15)) continue;
            var name = System.Text.Encoding.ASCII.GetString(bytes.AsSpan(0, end));
            if (name is not ("NVSEPlugin_Query" or "NVSEPlugin_Load")) continue;
            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(ordinalTable.AsSpan().Slice(checked((int)index * 2), 2));
            if (ordinal >= functions || !found.Add(name)) throw new InvalidDataException("Original NVSE export identity is duplicated or out of range.");
            var entry = BinaryPrimitives.ReadUInt32LittleEndian(functionTable.AsSpan().Slice(ordinal * 4, 4));
            if (entry == 0 || entry >= directory.RelativeVirtualAddress && entry < (long)directory.RelativeVirtualAddress + directory.Size)
                throw new NotSupportedException("Forwarded or absent NVSE Query/Load has no retained native module owner.");
            if (!pe.PEHeaders.SectionHeaders.Any(section => entry >= section.VirtualAddress &&
                entry < (long)section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData) &&
                section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute)))
                throw new InvalidDataException("Original NVSE export is not declared executable image code.");
        }
        if (found.Count != 2) throw new InvalidDataException("Original image lacks its genuine NVSEPlugin_Query/Load declarations.");
    }
    private static void RequireHash(string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Native source identities require exact SHA256 values.");
    }
    private static uint Pack(int major, int minor, int build, int sub)
    {
        if (major is < 0 or > 255 || minor is < 0 or > 255 || build is < 0 or > 4095 || sub is < 0 or > 15)
            throw new InvalidDataException("Owned native version is outside the public packed field extents.");
        return ((uint)major << 24) | ((uint)minor << 16) | ((uint)build << 4) | (uint)sub;
    }
}
