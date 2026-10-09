namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal string? NativePluginExecutionFailure => _scripts?.References?.NativePlugins is { } owner
        ? string.Join("; ", owner.Modules.Where(row => row.Query != true || row.Load != true || row.Failure is not null || row.Unowned.Count != 0)
            .Select(row => row.LogicalPath + ": Query=" + row.Query + ", Load=" + row.Load + "; " + (row.Failure ?? string.Join(", ", row.Unowned)))) is { Length: > 0 } errors ? errors : null
        : null;
    internal object? NativePluginExecutionState => _scripts?.References?.NativePlugins?.Modules;
}
