using System.ComponentModel;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Retain the actual installed Windows declarations. The selected module never
// receives executable retail bytes, a substitute Windows DLL or a heap object.
internal sealed class NativePluginMutexWindowsSource : IDisposable
{
    private sealed class InputLease(FileStream input)
    {
        internal FileStream Input { get; } = input;
        internal NativePluginSteamFileIdentity? Identity { get; set; }
    }
    private readonly List<InputLease> _inputs = [];
    private NativePluginImageDeclaration? _native;
    internal NativePluginImageDeclaration Native => _native ?? throw new InvalidOperationException("Mutex Windows source has not completed preparation.");
    private bool _disposed, _prepareEntered, _prepared;
    private Exception? _prepareFailure;
    internal void Prepare()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_prepared) { RequireCurrent(); return; }
        if (_prepareEntered) throw new InvalidOperationException("Entered mutex Windows preparation cannot replay its failed source prefix.", _prepareFailure);
        _prepareEntered = true;
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("The original x86 mutex namespace owner requires its actual x64 Windows parent.");
        try
        {
            var parentBase = Retain(Path.Combine(Environment.SystemDirectory, "kernelbase.dll"), Machine.Amd64);
            var parentNt = Retain(Path.Combine(Environment.SystemDirectory, "ntdll.dll"), Machine.Amd64);
            _native = Retain(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "ntdll.dll"), Machine.I386);
            _ = Export(parentBase, "BaseGetNamedObjectDirectory");
            foreach (var name in new[] { "NtCreateMutant", "NtOpenMutant", "RtlNtStatusToDosError" }) _ = Export(Native, name);
            RequireMappedParent(parentBase, "BaseGetNamedObjectDirectory");
            RequireMappedParent(parentNt, "NtOpenDirectoryObject");
            RequireMappedParent(parentNt, "NtQueryObject");
            RequireMappedParent(parentNt, "RtlNtStatusToDosError");
            _prepared = true;
        }
        catch (Exception error) { _prepareFailure = error; throw; }
    }
    private NativePluginImageDeclaration Retain(string path, Machine machine)
    {
        var input = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        var lease = new InputLease(input); _inputs.Add(lease);
        lease.Identity = NativePluginSteamFileIdentity.Read(input);
        var declaration = NativePluginImageDeclarations.Read(path, dll: true, requireX86: machine == Machine.I386);
        if (declaration.Machine != machine || declaration.Managed || declaration.Magic != (machine == Machine.I386 ? PEMagic.PE32 : PEMagic.PE32Plus))
            throw new InvalidDataException("Mutex Windows source has a different actual provider architecture.");
        return declaration;
    }
    internal static NativePluginImageExport Export(NativePluginImageDeclaration image, string name)
    {
        var matches = image.Exports.Where(value => value.Name == name).ToArray();
        if (matches.Length != 1 || !matches[0].Executable || matches[0].Forwarded || matches[0].Rva == 0)
            throw new NotSupportedException("Mutex Windows callable lacks its exact executable nonforwarded source export: " + name);
        return matches[0];
    }
    private void RequireMappedParent(NativePluginImageDeclaration source, string name)
    {
        var module = GetModuleHandleW(Path.GetFileName(source.Path));
        if (module == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Mutex parent Windows provider is absent.");
        var path = new StringBuilder(32768); var count = GetModuleFileNameW(module, path, (uint)path.Capacity);
        if (count == 0 || count >= path.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error(), "Mutex parent provider identity is incomplete.");
        using var actual = new FileStream(path.ToString(), FileMode.Open, FileAccess.Read, FileShare.Read);
        var expected = _inputs.Single(value => StringComparer.OrdinalIgnoreCase.Equals(value.Input.Name, source.Path));
        if (NativePluginSteamFileIdentity.Read(actual) != expected.Identity ||
            GetProcAddress(module, name) != checked(module + (nint)Export(source, name).Rva))
            throw new InvalidDataException("Mutex parent API differs from its retained actual Windows image/export.");
    }
    internal NativePluginSteamFileIdentity NativeIdentity => _inputs.Single(value =>
        StringComparer.OrdinalIgnoreCase.Equals(value.Input.Name, Native.Path)).Identity ??
        throw new InvalidOperationException("Windows source identity preparation is unfinished.");
    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_prepared) throw new InvalidOperationException("Mutex Windows source preparation remains unfinished.", _prepareFailure);
        foreach (var source in _inputs)
            if (NativePluginSteamFileIdentity.Read(source.Input) != source.Identity)
                throw new InvalidDataException("Retained mutex Windows source changed during its actual process generation.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        List<Exception> failures = [];
        for (var index = _inputs.Count - 1; index >= 0; --index)
            try { _inputs[index].Input.Dispose(); _inputs.RemoveAt(index); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Mutex Windows source retirement failed.", failures);
        _disposed = true;
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint GetModuleHandleW(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetModuleFileNameW(nint module, StringBuilder name, uint length);
    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] private static extern nint GetProcAddress(nint module, string name);
}
