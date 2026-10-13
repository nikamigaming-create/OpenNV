using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeNvseLoggingPath(string ConfigurationVirtualPath, string ConfigurationPhysicalPath,
    string? ConfigurationWinnerSha256, string? ConfigurationSha256, ImmutableArray<byte> ConfiguredBytes,
    ImmutableArray<byte> ReturnedBytes, string VirtualDirectory, string PrivateDirectory);
internal sealed record NativeNvseLoggingReceipt(ulong Callback, ulong Call, ulong Generation, ulong Module,
    uint Thread, uint Address, NativeNvseLoggingPath Source);

internal sealed partial class NativePluginExecutionDomain
{
    private NativeNvseLoggingPath? _nvseLogging;
    private uint _nvseLoggingAddress;
    private readonly List<NativeNvseLoggingReceipt> _nvseLoggingReceipts = [];
    internal IReadOnlyList<NativeNvseLoggingReceipt> NvseLoggingReceipts => _nvseLoggingReceipts.AsReadOnly();

    private NativeNvseLoggingPath PrepareNvseLogging(NativeNvseHostSource host, NativePluginPrivateIo io)
    {
        if (host.NvseVersion is not (0x06040080 or 0x06040090))
            throw new NotSupportedException("Selected xNVSE has no reviewed Logging interface ABI.");
        if (_nvseLogging is not null) throw new InvalidOperationException("Logging path already belongs to a native image.");
        return _nvseLogging = io.PrepareNvseLogging(Generation);
    }
    private uint DispatchNvseLogging(Frame frame, ulong parent, BinaryReader reader, NativeNvsePlugin plugin)
    {
        var address = reader.ReadUInt32(); var length = reader.ReadUInt32();
        if (address == 0 || length > NativeNvseLoggingSdk.MaximumReturnedBytes ||
            length != reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Logging getter has no complete native character extent.");
        var bytes = reader.ReadBytes(checked((int)length));
        var source = _nvseLogging ?? throw new NotSupportedException("Logging getter lost its selected configuration/path owner.");
        _privateIo!.RequireNvseLogging(Generation, source);
        if (!bytes.AsSpan().SequenceEqual(source.ReturnedBytes.AsSpan()) || _nvseLoggingAddress != 0 && address != _nvseLoggingAddress)
            throw new InvalidDataException("Logging getter changed its source bytes or retained native pointer.");
        _nvseLoggingAddress = address;
        _nvseLoggingReceipts.Add(new(frame.Id, parent, Generation, plugin.Module, NativeThread, address, source));
        return 1;
    }
}

internal sealed partial class NativePluginPrivateIo
{
    private NativeNvseLoggingPath? _nvseLoggingPath;
    private FileStream? _nvseLoggingConfigurationLease;
    private int _nvseLoggingManagedThread;

    internal NativeNvseLoggingPath PrepareNvseLogging(ulong generation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_generation != generation || _nvseLoggingPath is not null)
            throw new InvalidOperationException("Logging source requires this unbound native generation.");
        _nvseLoggingManagedThread = Environment.CurrentManagedThreadId;
        var config = Path.Combine(Selection.RuntimeDirectory, "Data", "NVSE", "nvse_config.ini");
        var route = Resolve(0, 8, NativePluginIoAction.ProfileRead, config);
        if (route.Error is not (0 or 2) || route.PhysicalPath is null)
            throw new NotSupportedException("Logging configuration has no genuine selected profile source or source absence.");
        if (route.Role is not null)
            throw new NotSupportedException("Global xNVSE logging configuration has no module-private configuration producer.");
        FileStream? lease = null;
        try
        {
            string? sha = null;
            if (route.Error == 0)
            {
                lease = new(route.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                sha = Convert.ToHexString(SHA256.HashData(lease)); lease.Position = 0;
            }
            var configured = NativeNvseLoggingSdk.ReadConfiguration(route.PhysicalPath, out var profileLastError);
            ProfileResult(0, route.Id, NativePluginProfileKind.StringA, NativeNvseLoggingSdk.ProfileCapacity,
                checked((uint)configured.Length), profileLastError, false);
            var returned = NativeNvseLoggingSdk.ReturnedPath(configured);
            var text = NativeNvseLoggingSdk.Decode(returned);
            var directory = text.Length == 0 ? Canonical(Selection.RuntimeDirectory) : Virtual(text);
            if (!Within(Canonical(Selection.RuntimeDirectory), directory))
                throw new NotSupportedException("Logging directory escaped its source game folder.");
            NoReparse(directory);
            var identity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(directory.ToUpperInvariant())));
            var privateDirectory = Path.Combine(ModuleRoot, NativePluginIoRole.Diagnostic.ToString(), "Logging", identity);
            NoReparse(privateDirectory); Directory.CreateDirectory(privateDirectory); NoReparse(privateDirectory);
            _nvseLoggingConfigurationLease = lease; lease = null;
            return _nvseLoggingPath = new(Canonical(config), route.PhysicalPath, route.WinnerSha256, sha,
                ImmutableArray.CreateRange(configured), ImmutableArray.CreateRange(returned), directory, privateDirectory);
        }
        finally { lease?.Dispose(); }
    }
    internal void RequireNvseLogging(ulong generation, NativeNvseLoggingPath path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_generation != generation || Environment.CurrentManagedThreadId != _nvseLoggingManagedThread ||
            !ReferenceEquals(path, _nvseLoggingPath))
            throw new InvalidOperationException("Logging source lost its actual generation/thread/path lifetime.");
        if (_nvseLoggingConfigurationLease is { } lease)
        {
            lease.Position = 0;
            if (!Convert.ToHexString(SHA256.HashData(lease)).Equals(path.ConfigurationSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Logging configuration changed during its native lifetime.");
            lease.Position = 0;
        }
        else if (Selection.ResolveWinningRead(path.ConfigurationVirtualPath) is not null ||
            File.Exists(path.ConfigurationVirtualPath) || File.Exists(path.ConfigurationPhysicalPath))
            throw new InvalidDataException("Logging source absence changed during its native lifetime.");
        NoReparse(path.PrivateDirectory);
        if (!Directory.Exists(path.PrivateDirectory)) throw new InvalidDataException("Logging diagnostic directory lost its product owner.");
    }
    private void DisposeNvseLoggingSource()
    {
        _nvseLoggingConfigurationLease?.Dispose(); _nvseLoggingConfigurationLease = null; _nvseLoggingPath = null;
    }
}

// xNVSE 6.4.8/6.4.9 PluginAPI.h, nvse.cpp and Utilities.cpp establish the
// one-pointer fastcall ABI and the selected ANSI profile/path producer.
// Reviewed source: github.com/xNVSE/NVSE/tree/6.4.8/nvse/nvse
// and github.com/xNVSE/NVSE/tree/6.4.9/nvse/nvse.
// The returned bytes stay relative to the game folder; private I/O routes
// separately own physical diagnostics. No installation directory is created.
internal static class NativeNvseLoggingSdk
{
    internal const uint ProfileCapacity = 255;
    internal const uint MaximumReturnedBytes = ProfileCapacity;
    internal static byte[] ReadConfiguration(string file) => ReadConfiguration(file, out _);
    internal static byte[] ReadConfiguration(string file, out uint lastError)
    {
        var buffer = new byte[ProfileCapacity + 1];
        var count = GetPrivateProfileStringA("LOGGING\0"u8.ToArray(), "sPluginLogPath\0"u8.ToArray(), null,
            buffer, ProfileCapacity, Encode(file));
        lastError = unchecked((uint)Marshal.GetLastPInvokeError());
        if (count >= ProfileCapacity || buffer[count] != 0)
            throw new InvalidDataException("Logging profile result exceeded its original character extent.");
        return buffer.AsSpan(0, checked((int)count)).ToArray();
    }
    internal static byte[] ReturnedPath(ReadOnlySpan<byte> configured)
    {
        if (configured.Length >= ProfileCapacity || configured.Contains((byte)0))
            throw new InvalidDataException("Logging profile bytes have no original NUL-terminated extent.");
        var path = Decode(configured);
        var leaf = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        // std::filesystem::path's final component has no extension for .name,
        // . or .., but name. has the extension ".". A trailing slash is empty.
        if (leaf is not ("." or "..") && leaf.LastIndexOf('.') > 0) return [];
        if (path.Length == 0) return [];
        if (Path.IsPathRooted(path) || path.Contains(':') || path.Any(char.IsControl) ||
            path.Split(['\\', '/']).Any(part => part is "." or ".."))
            throw new NotSupportedException("Logging path has an unowned rooted/traversal/control identity.");
        return [.. configured, (byte)'\\'];
    }
    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return "";
        var source = bytes.ToArray(); var count = MultiByteToWideChar(GetACP(), 8, source, source.Length, null, 0);
        if (count == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var characters = new char[count];
        if (MultiByteToWideChar(GetACP(), 8, source, source.Length, characters, count) != count)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(characters);
    }
    private static byte[] Encode(string value)
    {
        var codePage = GetACP(); var usedDefault = false;
        var flags = codePage == 65001 ? 0x80U : 0x400U;
        var count = codePage == 65001 ? WideCharToMultiByteUtf8(codePage, flags, value, value.Length, null, 0, IntPtr.Zero, IntPtr.Zero) :
            WideCharToMultiByte(codePage, flags, value, value.Length, null, 0, IntPtr.Zero, ref usedDefault);
        if (count == 0 || usedDefault) throw new NotSupportedException("Logging configuration path has no exact native ANSI identity.");
        var bytes = new byte[count + 1]; usedDefault = false;
        var written = codePage == 65001 ? WideCharToMultiByteUtf8(codePage, flags, value, value.Length, bytes, count, IntPtr.Zero, IntPtr.Zero) :
            WideCharToMultiByte(codePage, flags, value, value.Length, bytes, count, IntPtr.Zero, ref usedDefault);
        if (written != count || usedDefault)
            throw new NotSupportedException("Logging configuration path changed its native ANSI identity.");
        return bytes;
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetPrivateProfileStringA(byte[] section, byte[] key, byte[]? fallback, [Out] byte[] output, uint capacity, byte[] file);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GetACP();
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MultiByteToWideChar(uint page, uint flags, byte[] bytes, int length, [Out] char[]? characters, int capacity);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WideCharToMultiByte(uint page, uint flags, string characters, int length, [Out] byte[]? bytes,
        int capacity, IntPtr fallback, [MarshalAs(UnmanagedType.Bool)] ref bool usedDefault);
    [DllImport("kernel32.dll", EntryPoint = "WideCharToMultiByte", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WideCharToMultiByteUtf8(uint page, uint flags, string characters, int length, [Out] byte[]? bytes,
        int capacity, IntPtr fallback, IntPtr usedDefault);
}
