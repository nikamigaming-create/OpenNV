namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginModuleDiagnosticOpen(ulong Ordinal, ulong Generation, int Process,
    uint Thread, ulong Parent, ulong Module, string SourceSha256, string StackSha256, string Family,
    uint Caller, uint Cell, uint CellValue, uint Attempt, int Sharing, string Path, string Mode,
    ulong Route, uint? Stream, NativePluginCrtStatus? Status, bool Returned);

internal sealed partial class NativePluginExecutionDomain
{
    private readonly Dictionary<ulong, NativePluginModuleDiagnosticCrtSource> _moduleDiagnosticSources = [];
    private readonly List<NativePluginModuleDiagnosticOpen> _moduleDiagnosticOpens = [];
    private readonly Dictionary<ulong, int> _moduleDiagnosticLastOpen = [];
    internal IReadOnlyList<NativePluginModuleDiagnosticOpen> NvseModuleDiagnosticOpens => _moduleDiagnosticOpens.AsReadOnly();
    private static void WriteModuleDiagnosticSource(BinaryWriter writer, NativePluginModuleDiagnosticCrtSource? source)
    {
        writer.Write(source is null ? 0U : 1U);
        if (source is null) return;
        WriteText(writer, source.Sha256); WriteText(writer, source.StackSha256); WriteText(writer, source.Family);
        writer.Write(source.QueryRva); writer.Write(source.ProducerRva); writer.Write(source.StreamRva);
        writer.Write(source.NameRva); writer.Write(source.ModeRva); writer.Write(source.FirstReturnRva); writer.Write(source.RetryReturnRva);
        writer.Write(source.Sharing); writer.Write(source.RetryLimit); WriteText(writer, source.Name); WriteText(writer, source.Mode);
    }
    private bool HasModuleDiagnosticCallback(ulong module, uint callback) =>
        callback is CrtProvider or CrtRoute or CrtOpened or CrtEvent or CrtSupportCallback && _moduleDiagnosticSources.ContainsKey(module);
    private NativePluginIoRoute EnterModuleDiagnostic(ulong parent, ulong module, uint caller, uint cell,
        uint value, uint attempt, int sharing, uint api, string path, string mode, bool writable)
    {
        var plugin = RetainedNvseModule(module);
        var source = _moduleDiagnosticSources[module]; var io = _privateIo!;
        var index = checked((ulong)_moduleDiagnosticOpens.Count + 1);
        var entered = new NativePluginModuleDiagnosticOpen(index, Generation, ProcessId, NativeThread, parent, module,
            source.Sha256, source.StackSha256, source.Family, caller, cell, value, attempt, sharing, path, mode, 0, null, null, false);
        var previous = _moduleDiagnosticLastOpen.TryGetValue(module, out var previousAt) ? _moduleDiagnosticOpens[previousAt] : null;
        _moduleDiagnosticLastOpen[module] = _moduleDiagnosticOpens.Count; _moduleDiagnosticOpens.Add(entered);
        source.Check(_nvseModuleDeclarations[module], io.Selection.StackSha256);
        if (plugin.Phase != NativeNvsePhase.Querying || api != 1 || !writable || value != 0 ||
            (ulong)plugin.Image + source.StreamRva != cell ||
            (ulong)plugin.Image + (attempt == 0 ? source.FirstReturnRva : source.RetryReturnRva) != caller ||
            (attempt == 0 ? previous is not null : previous is null || !previous.Returned || previous.Stream != 0 || previous.Attempt + 1 != attempt))
            throw new NotSupportedException("Diagnostic CRT lost its exact original Query/null-result/global/caller order.");
        var route = io.ResolveModuleDiagnostic(parent, source, path, mode, sharing, attempt);
        _moduleDiagnosticOpens[^1] = entered with { Route = route.Id }; return route;
    }
    private void CompleteModuleDiagnostic(ulong module, ulong route, uint stream, NativePluginCrtStatus status)
    {
        if (!_moduleDiagnosticLastOpen.TryGetValue(module, out var at) || _moduleDiagnosticOpens[at].Route != route || _moduleDiagnosticOpens[at].Returned)
            throw new InvalidDataException("Diagnostic CRT result lacks its entered original path/mode/share producer.");
        _moduleDiagnosticOpens[at] = _moduleDiagnosticOpens[at] with { Stream = stream, Status = status, Returned = true };
    }
    private void RequireModuleDiagnosticSaveOwned()
    {
        if (_moduleDiagnosticSources.Count != 0 || _moduleDiagnosticOpens.Count != 0)
            throw new NotSupportedException("Original module diagnostic FILE/global/error/constructor state has no complete cold owner.");
    }
}
