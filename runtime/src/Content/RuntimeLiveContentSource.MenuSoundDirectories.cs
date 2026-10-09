using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMenuSoundDirectory(string RawPath, string Extension,
    IReadOnlyList<string> Paths, string OrderSha256, string ProducerSha256);

internal sealed partial class RuntimeLiveContentSource
{
    private readonly object _menuSoundGate = new();
    private FalloutMenuSoundSelection? _menuSoundSelection;
    private bool _menuSoundRetiring;
    internal FalloutMenuSoundSelection OpenMenuSoundSelection(FalloutPluginStack records,
        FalloutAdvancementRuntimeSource runtime, FalloutMenuSoundSelectionSnapshot? restore = null)
    {
        lock (_menuSoundGate)
        {
            ObjectDisposedException.ThrowIf(_menuSoundRetiring, this);
            if (!ReferenceEquals(records.OwnedSource, this) || !ReferenceEquals(runtime.OwnedSource, this))
                throw new InvalidDataException("Menu sound selection differs from its actual selected graph/runtime.");
            var declaration = FalloutMenuSoundSelectionSource.Read(runtime);
            if (_menuSoundSelection is { } existing)
            {
                existing.RequireOwner(records, declaration);
                if (restore is not null) existing.RequireSameSnapshot(restore);
                return existing;
            }
            try
            {
                if (restore is not null && restore.FileManager is null)
                    throw new InvalidDataException("Owned sound selector lost its current file-manager modes/prefix.");
                _soundFileManager = OpenSoundFileManager(declaration, restore?.FileManager);
                return _menuSoundSelection = new(records, declaration, ReadMenuSoundDirectory, restore,
                    requireSourceDirectory: RequireActualMenuSoundDirectory,
                    captureFileManager: _soundFileManager.Capture);
            }
            catch (Exception original)
            {
                try { RetireSoundFileManager(); }
                catch (Exception cleanup) { throw new AggregateException("Sound source construction and retirement failed.", original, cleanup); }
                throw;
            }
        }
    }

    internal FalloutMenuSoundSelection RequireMenuSoundSelection(FalloutPluginStack records)
    {
        lock (_menuSoundGate)
        {
            ObjectDisposedException.ThrowIf(_menuSoundRetiring, this);
            var owner = _menuSoundSelection ?? throw new NotSupportedException("Menu cue requires the real selected sound/RNG lifetime before presentation.");
            owner.RequireOwner(records, owner.Source); return owner;
        }
    }

    private FalloutMenuSoundDirectory ReadMenuSoundDirectory(string raw, string extension)
        => (_soundFileManager ?? throw new NotSupportedException("Directory read has no actual file-manager startup lifetime.")).ReadDirectory(raw, extension);

    private FalloutLooseSoundDirectory ReadActualLooseSoundDirectory(FalloutArchiveStartupModes modes, string raw, string extension)
    {
        if (!OperatingSystem.IsWindows() || GetACP() != 1252)
            throw new NotSupportedException("Source directory order requires its actual Windows ANSI file enumerator.");
        var roots = RequireStartupSoundSearchRoots(modes);
        var prefix = FalloutMenuSoundSelectionSource.LogicalPath(raw).TrimEnd('\\') + "\\";
        var directory = FindActualDirectory(roots[1], prefix.TrimEnd('\\'));
        IReadOnlyList<string> names = directory is null ? [] : ReadNativeNames(Path.Combine(directory, "*" + extension));
        var paths = names.Reverse().Select(name => FalloutBsaArchive.CanonicalPath(prefix + name)).ToArray();
        var producer = FalloutAdvancementRuntimeReceipt.Hash("Win32-FindFirstFileA-FindNextFileA-FindClose;1252;direct-child;prepend;" +
            StackId + "\0" + roots[1]);
        return new(roots[1], Array.AsReadOnly(paths), producer);
    }

    private void RequireActualMenuSoundDirectory(FalloutMenuSoundDirectory saved)
        => (_soundFileManager ?? throw new NotSupportedException("Cold directory has no current file-manager source.")).RequireColdDirectory(saved);

    private static string? FindActualDirectory(string root, string canonical)
    {
        var current = root;
        foreach (var segment in canonical.Split('\\'))
        {
            var matches = Directory.EnumerateFileSystemEntries(current).Where(path =>
                Path.GetFileName(path).Equals(segment, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) return null;
            if (matches.Length != 1) throw new InvalidDataException("Sound directory has an ambiguous case-insensitive source name.");
            current = matches[0]; if (!Directory.Exists(current)) return null;
        }
        return current;
    }

    private static IReadOnlyList<string> ReadNativeNames(string pattern)
    {
        _ = FalloutMenuSoundSelectionSource.PathBytes(pattern);
        var handle = FindFirstFile(pattern, out var data);
        if (handle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 2 or 3 or 18) return [];
            throw new Win32Exception(error, "Actual source sound directory enumeration did not open.");
        }
        Exception? failure = null; var names = new List<string>();
        try
        {
            do
            {
                var name = data.Name;
                if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains('/') || name.Contains('\0'))
                    throw new InvalidDataException("Native sound directory returned an invalid direct-child name.");
                names.Add(name);
            } while (FindNextFile(handle, out data));
            var error = Marshal.GetLastWin32Error();
            if (error != 18) throw new Win32Exception(error, "Actual source sound directory enumeration stopped before its end.");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (!FindClose(handle))
            {
                var cleanup = new Win32Exception(Marshal.GetLastWin32Error(), "Original sound directory handle did not close.");
                if (failure is not null) failure = new AggregateException("Sound directory retained enumeration and handle-retirement failures.", failure, cleanup);
                else failure = cleanup;
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return names;
    }

    internal void RetireMenuSoundSelection()
    {
        lock (_menuSoundGate)
        {
            _menuSoundRetiring = true;
            Exception? failure = null;
            try { _menuSoundSelection?.Retire(); }
            catch (Exception error) { failure = error; }
            try { RetireSoundFileManager(); }
            catch (Exception cleanup)
            {
                if (failure is not null) throw new AggregateException("Sound selection and file-manager retirement failed.", failure, cleanup);
                throw;
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct SoundFindData
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        internal uint SizeHigh, SizeLow, Reserved0, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] internal string Alternate;
    }
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GetACP();
    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileA", ExactSpelling = true, CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr FindFirstFile(string pattern, out SoundFindData data);
    [DllImport("kernel32.dll", EntryPoint = "FindNextFileA", ExactSpelling = true, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindNextFile(IntPtr handle, out SoundFindData data);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindClose(IntPtr handle);
}
