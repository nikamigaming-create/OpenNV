using System.ComponentModel;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    private readonly List<NativePluginKernelHandle> _failedWindowProcessReferences = [];
    internal bool WindowProcessReferencesRetired => _failedWindowProcessReferences.Count == 0;
    internal NativePluginKernelHandle RetainWindowProcessReference(uint source)
    {
        if (HasExited || ResourcesRetired || source is 0 or uint.MaxValue)
            throw new InvalidOperationException("Process query reference lost its exact live child creation handle.");
        if (!DuplicateFromOriginal(CngCreationHandle, (nint)(nuint)source, CurrentProcess(), out var result, 0, false, 2))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Actual source query handle could not retain its kernel identity.");
        return new(result);
    }
    internal void RetainFailedWindowProcessReference(NativePluginKernelHandle reference) => _failedWindowProcessReferences.Add(reference);
    internal void RequireWindowProcessIdentity(uint handle, NativePluginKernelHandle guard)
    {
        var observed = RetainWindowProcessReference(handle);
        try
        {
            if (!CompareSections(observed, guard)) throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "Actual source query handle no longer denotes its retained kernel object.");
        }
        finally { try { CloseWindowProcessReference(observed); } catch { _failedWindowProcessReferences.Add(observed); throw; } }
    }
    internal void CloseWindowProcessReference(NativePluginKernelHandle reference)
    {
        if (!NativePluginIoSecurity.CloseHandle(reference.DangerousGetHandle()))
        { throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Actual query identity reference failed independent closure."); }
        reference.SetHandleAsInvalid(); reference.Dispose();
    }
    internal void RequireWindowProcessReferencesRetired()
    {
        if (_failedWindowProcessReferences.Count != 0) throw new InvalidOperationException("Native query verification references remain after independent retirement.");
    }
    internal void CloseFailedWindowProcessReferencesAfterChildExit()
    {
        if (!HasExited) throw new InvalidOperationException("Failed query reference cleanup requires exact original child closure.");
        List<Exception> failures = [];
        foreach (var reference in _failedWindowProcessReferences.ToArray())
        {
            if (!NativePluginIoSecurity.CloseHandle(reference.DangerousGetHandle()))
            { failures.Add(new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error())); continue; }
            reference.SetHandleAsInvalid(); reference.Dispose(); _failedWindowProcessReferences.Remove(reference);
        }
        if (failures.Count != 0) throw new AggregateException("Independent query verification closure failed.", failures);
    }
}
