using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    private readonly List<NativePluginKernelHandle> _failedMutexVerificationRefs = [];
    private readonly List<NativePluginMutexComparisonReceipt> _mutexComparisons = [];
    internal IReadOnlyList<NativePluginMutexComparisonReceipt> MutexComparisons => _mutexComparisons.AsReadOnly();
    internal void RequireMutexLiteral(uint image, uint address, byte[] bytes)
    {
        if (ResourcesRetired || HasExited || image == 0 || address < image || bytes.Length == 0 ||
            (ulong)address + (uint)bytes.Length > 1UL << 32)
            throw new InvalidOperationException("Mutex name readback lost its actual living original image.");
        var observed = new byte[bytes.Length];
        var cursor = address; var remaining = checked((uint)bytes.Length);
        while (remaining != 0)
        {
            if (QueryOriginal(CngCreationHandle, (nint)(nuint)cursor, out var memory, (nuint)Marshal.SizeOf<CngMemoryInformation>()) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var end = (ulong)(nuint)memory.Base + (ulong)memory.Region;
            if ((nuint)memory.AllocationBase != image || memory.Type != 0x1000000 || memory.State != 0x1000 ||
                memory.Protection is not (2 or 0x20) || (nuint)memory.Base > cursor || end <= cursor)
                throw new InvalidDataException("Mutex name is not a complete readonly borrowed extent of the actual original image.");
            var count = checked((uint)Math.Min(end - cursor, remaining)); remaining -= count;
            if (remaining != 0) cursor = checked(cursor + count);
        }
        if (!ReadEngineMemory(CngCreationHandle, (nint)(nuint)address, observed, (nuint)observed.Length, out var read) || read != (nuint)observed.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Mutex source string has no complete actual child readback.");
        if (!observed.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Original mapped mutex name differs from the retained source bytes.");
    }
    internal ulong? MatchOwnedMutexObject(uint handle, IReadOnlyList<NativePluginMutexHandle> known)
    {
        RequireMutexProcess();
        var created = DuplicateMutexRef(handle);
        try
        {
            ulong? matched = null;
            foreach (var previous in known)
            {
                var candidate = DuplicateMutexRef(previous.Handle);
                try
                {
                    // The existing partial imports the real Kernelbase export,
                    // with SetLastError=true. A failed API is not a non-alias.
                    var same = CompareSections(created, candidate); var error = unchecked((uint)Marshal.GetLastWin32Error());
                    _mutexComparisons.Add(new(handle, previous.Handle, previous.Capability, same, error));
                    if (same) matched ??= previous.Capability;
                    else if (error != 1656) throw new Win32Exception(unchecked((int)error), "Actual kernel-object comparison failed independently of identity.");
                }
                finally { CloseMutexVerificationRef(candidate); }
            }
            return matched;
        }
        finally { CloseMutexVerificationRef(created); }
    }
    private NativePluginKernelHandle DuplicateMutexRef(uint source)
    {
        if (source is 0 or uint.MaxValue) throw new InvalidDataException("Mutex comparison received a null/pseudo handle.");
        if (!DuplicateFromOriginal(CngCreationHandle, (nint)(nuint)source, CurrentProcess(), out var retained, 0, false, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual mutex identity comparison could not borrow the child kernel reference.");
        return new(retained);
    }
    private void CloseMutexVerificationRef(NativePluginKernelHandle borrowed)
    {
        if (!NativePluginIoSecurity.CloseHandle(borrowed.DangerousGetHandle()))
        {
            _failedMutexVerificationRefs.Add(borrowed);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Temporary mutex identity reference failed actual independent CloseHandle.");
        }
        borrowed.SetHandleAsInvalid(); borrowed.Dispose();
    }
    private void RequireMutexProcess()
    {
        if (ResourcesRetired || HasExited) throw new InvalidOperationException("Mutex identity comparison has no actual living creation handle.");
        if (_failedMutexVerificationRefs.Count != 0) throw new InvalidOperationException("A previous mutex verification reference failed retirement.");
    }
    internal void RequireMutexVerificationRetired()
    {
        if (_failedMutexVerificationRefs.Count != 0)
            throw new InvalidDataException("Actual mutex identity reference retirement remains failed.");
    }
    internal void CloseMutexVerificationAfterChildExit()
    {
        if (!HasExited) throw new InvalidOperationException("Failed mutex verification cleanup requires confirmed original child closure.");
        var failures = new List<Exception>();
        for (var at = _failedMutexVerificationRefs.Count - 1; at >= 0; --at)
        {
            var reference = _failedMutexVerificationRefs[at];
            if (!NativePluginIoSecurity.CloseHandle(reference.DangerousGetHandle()))
            {
                failures.Add(new Win32Exception(Marshal.GetLastWin32Error(), "Failed mutex verification reference still has not closed."));
                continue;
            }
            reference.SetHandleAsInvalid(); reference.Dispose(); _failedMutexVerificationRefs.RemoveAt(at);
        }
        if (failures.Count != 0) throw new AggregateException("Independent mutex observation reference retirement failed.", failures);
    }
}
