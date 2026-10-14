using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginPrivateIo
{
    private FileStream? _parallelHardwareSource;
    internal NativePluginParallelHardwareSelection? ParallelHardwareProvider { get; private set; }
    private void InitializeParallelHardwareProvider()
    {
        var selection = NativePluginParallelHardwareImports.Read(Selection.ModulePath, Selection.ModuleSha256);
        if (selection is null) return;
        NoReparse(selection.Path);
        var lease = new FileStream(selection.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _parallelHardwareSource = lease; ParallelHardwareProvider = selection;
        RequireParallelHardwareSourceCurrent();
    }
    internal void RequireParallelHardwareSourceCurrent()
    {
        if (ParallelHardwareProvider is not { } selection) return;
        var lease = _parallelHardwareSource ?? throw new InvalidDataException("Parallel hardware provider lost its source lease.");
        var position = lease.Position;
        try
        {
            lease.Position = 0;
            if (!Convert.ToHexString(SHA256.HashData(lease)).Equals(selection.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Parallel hardware provider source changed during its native lifetime.");
        }
        finally { lease.Position = position; }
    }
    private void DisposeParallelHardwareSource()
    {
        _parallelHardwareSource?.Dispose(); _parallelHardwareSource = null;
    }
}
