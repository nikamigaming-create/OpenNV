namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    // In-process neutral source declarations belong to this exact selection.
    // No serialized address/decoded object table becomes a launch input.
    private FalloutNativePluginCampaignSelection? _nativePluginDeclarations;
    private FalloutNativePluginHostDependency? _nativePluginHostDependency;
    private readonly object _nativePluginDeclarationGate = new();
    private Task<FalloutNativePluginCampaignSelection>? _nativePluginDeclarationRead;
    private bool _nativePluginDeclarationRetiring;
    private Exception? _nativePluginDeclarationReadFailure;
    internal void BindNativePluginHostDependency(FalloutNativePluginHostDependency dependency)
    {
        lock (_nativePluginDeclarationGate)
        {
            ObjectDisposedException.ThrowIf(_nativePluginDeclarationRetiring, this);
            if (_nativePluginDeclarations is not null || _nativePluginDeclarationRead is not null || _nativePluginHostDependency is not null ||
                string.IsNullOrWhiteSpace(dependency.SourceOwner) || !File.Exists(dependency.PhysicalPath))
                throw new InvalidOperationException("Native host dependency is absent or already selected.");
            _nativePluginHostDependency = dependency with { PhysicalPath = Path.GetFullPath(dependency.PhysicalPath) };
        }
    }
    private Task<FalloutNativePluginCampaignSelection> ReadNativePluginDeclarations()
    {
        lock (_nativePluginDeclarationGate)
        {
            ObjectDisposedException.ThrowIf(_nativePluginDeclarationRetiring, this);
            if (_nativePluginDeclarations is { } ready) return Task.FromResult(ready);
            var dependency = _nativePluginHostDependency;
            // This task owns only read-only C# discovery. Native execution and
            // Godot construction remain with the actual product caller.
            return _nativePluginDeclarationRead ??= Task.Run(() =>
            {
                var declaration = FalloutNativePluginStartupDeclarations.Build(this, dependency);
                lock (_nativePluginDeclarationGate)
                {
                    if (_nativePluginDeclarations is not null || !ReferenceEquals(declaration.Source, this))
                        throw new InvalidOperationException("Native plugin discovery changed its selected source owner.");
                    _nativePluginDeclarations = declaration;
                }
                return declaration;
            });
        }
    }

    internal FalloutNativePluginCampaignSelection EnsureNativePluginDeclarations() =>
        RequireCurrentNativePluginDeclarations(ReadNativePluginDeclarations().GetAwaiter().GetResult());

    internal async Task<FalloutNativePluginCampaignSelection> EnsureNativePluginDeclarationsAsync() =>
        RequireCurrentNativePluginDeclarations(await ReadNativePluginDeclarations().ConfigureAwait(false));

    private FalloutNativePluginCampaignSelection RequireCurrentNativePluginDeclarations(FalloutNativePluginCampaignSelection declaration)
    {
        lock (_nativePluginDeclarationGate)
        {
            ObjectDisposedException.ThrowIf(_nativePluginDeclarationRetiring, this);
            if (!ReferenceEquals(_nativePluginDeclarations, declaration))
                throw new InvalidOperationException("Native plugin discovery lost its current selected source.");
            return declaration;
        }
    }

    internal FalloutNativePluginCampaignSelection? NativePluginDeclarations
    {
        get { lock (_nativePluginDeclarationGate) return _nativePluginDeclarations; }
    }

    internal void BindNativePluginDeclarations(FalloutNativePluginCampaignSelection declaration)
    {
        lock (_nativePluginDeclarationGate)
        {
            ObjectDisposedException.ThrowIf(_nativePluginDeclarationRetiring, this);
            if (_nativePluginDeclarations is not null || _nativePluginDeclarationRead is not null || !ReferenceEquals(declaration.Source, this))
                throw new InvalidOperationException("Native plugin declarations are foreign or already bound to the selected source.");
            _nativePluginDeclarations = declaration;
        }
    }

    private void RetireNativePluginDeclarationRead()
    {
        Task<FalloutNativePluginCampaignSelection>? read;
        lock (_nativePluginDeclarationGate)
        {
            _nativePluginDeclarationRetiring = true;
            read = _nativePluginDeclarationRead;
        }
        // Source replacement must wait for the genuine reader before releasing
        // its archives. A failed read keeps its original fault and is not retried.
        try { read?.GetAwaiter().GetResult(); }
        catch (Exception error) when (read is { IsFaulted: true }) { _nativePluginDeclarationReadFailure ??= error; }
    }
}
