using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginCngSharedSource(uint Kind, ulong Object, ulong View, uint Handle,
    uint Address, uint Length, uint Maximum, uint Offset);
internal sealed record NativePluginCngSharedPointer(ulong Section, ulong View, uint Offset, uint Length);
internal sealed record NativePluginCngSharedReceipt(ulong OriginalGeneration, ulong OriginalCall,
    ulong ServiceGeneration, int ServiceProcess, ulong Section, NativePluginCngSharedSource Source,
    uint ServiceAddress, uint RemoteHandle, bool ServiceViewRetired, bool ParentHandleRetired);
internal sealed record NativePluginCngSharedCallReceipt(ulong OriginalCall, ulong Invocation,
    NativePluginCryptoOperation Operation, NativePluginCngServiceResult Result,
    IReadOnlyList<NativePluginCngSharedPointer?> Buffers, NativePluginCngSharedPointer? Object,
    NativePluginCngSharedPointer? Copied);
internal sealed record NativePluginCngDetachReceipt(ulong OriginalGeneration, ulong OriginalModule,
    ulong Call, uint Image, bool Entered, bool Returned, bool FreeLibrarySucceeded, uint Error);

// Explicit checked close preserves a failed independent parent reference.
// SafeHandle's terminal fallback is not an orderly lifetime receipt.
internal sealed class NativePluginCngSectionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativePluginCngSectionHandle(nint value) : base(true) => SetHandle(value);
    internal void CloseChecked()
    {
        if (IsClosed || IsInvalid) return;
        if (!CloseSection(handle)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CNG parent section reference did not close.");
        SetHandleAsInvalid(); Dispose();
    }
    protected override bool ReleaseHandle() => CloseSection(handle);
    [DllImport("kernel32", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseSection(nint value);
}

internal sealed class NativePluginCngSharedSection(ulong id, NativePluginCngSharedSource source,
    NativePluginCngSectionHandle parent)
{
    internal ulong Id { get; } = id;
    internal uint Kind { get; } = source.Kind;
    internal ulong Object { get; } = source.Object;
    internal uint Maximum { get; } = source.Maximum;
    internal NativePluginCngSectionHandle Parent { get; } = parent;
    internal Dictionary<ulong, NativePluginCngSharedSource> Views { get; } = [];
    internal Dictionary<ulong, NativePluginCngSectionHandle> References { get; } = [];
    internal Dictionary<ulong, uint> ServiceAddresses { get; } = [];
    internal uint RemoteHandle { get; set; }
    internal uint ServiceAddress { get; set; }
    internal bool Entered { get; set; }
    internal bool ReleaseEntered { get; set; }
    internal bool ServiceViewRetired { get; set; }
    internal bool ParentHandleRetired { get; set; }
}
