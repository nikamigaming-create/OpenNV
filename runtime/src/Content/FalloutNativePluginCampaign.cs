using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// One actual campaign owns original-module generations, command identities,
// deferred compiled callers, local/value stores, faults and retirement. This
// is shared by bootstrap, quest recurrence, result scripts and resident actors.
internal sealed partial class FalloutNativePluginCampaign : IFalloutNativePluginCampaign
{
    private sealed class Module(FalloutNativePluginModuleAdmission admission, NativePluginExecutionDomain domain, NativeNvsePlugin plugin)
    {
        internal readonly FalloutNativePluginModuleAdmission Admission = admission;
        internal readonly NativePluginExecutionDomain Domain = domain;
        internal readonly NativeNvsePlugin Plugin = plugin;
        internal readonly Dictionary<FalloutFormKey, NativeNvseSourceObject> Objects = new(FalloutFormKeyComparer.Instance);
        internal readonly Dictionary<FalloutFormKey, NativeNvseLocalContext> Locals = new(FalloutFormKeyComparer.Instance);
        internal bool Retired;
        internal readonly Dictionary<string, NativeNvseSourceObject> GraphObjects = new(StringComparer.Ordinal);
        internal readonly List<NativeNvseSourceGraph> Graphs = [];
        internal string? ObjectFailure;
    }
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly RuntimeLiveContentSource _source;
    private readonly FalloutPluginStack _records;
    private readonly FalloutQuestState _quests;
    private readonly FalloutQuestScripts _scripts;
    private readonly FalloutReferenceWorld _world;
    private readonly FalloutNativePluginCampaignSelection? _selection;
    private readonly FalloutNativePluginDataBindings? _dataBindings;
    private readonly List<Module> _modules = [];
    private readonly List<NativePluginExecutionDomain> _retiringDomains = [];
    private readonly List<NativePluginPrivateIo> _retiringIos = [];
    private readonly List<FalloutNativePluginCampaignModule> _failures = [];
    private readonly Dictionary<ushort, (Module Module, NativeNvseCommand Command)> _commands = [];
    private bool _disposed;
    private int _active;
    internal bool ChildDomainsExited => _modules.All(module => module.Domain.ChildExited) &&
        _retiringDomains.All(domain => domain.ChildExited) && _retiringIos.All(io => io.FailedChildrenExited);
    internal bool ResourcesRetired => _disposed && _graphSource is null && _retiringIos.Count == 0 &&
        _modules.All(module => module.Domain.ResourcesRetired) && _retiringDomains.All(domain => domain.ResourcesRetired);
    internal string? ModuleFailure => string.Join("; ", Modules.Where(module => module.Query != true || module.Load != true ||
        module.Failure is not null || module.Unowned.Count != 0).Select(module => module.LogicalPath + ": Query=" + module.Query +
            ", Load=" + module.Load + "; " + (module.Failure ?? string.Join(", ", module.Unowned)))) is { Length: > 0 } failure ? failure : null;

    internal FalloutNativePluginCampaign(FalloutPluginStack records, FalloutQuestState quests,
        FalloutQuestScripts scripts, string companion, string privateStateRoot, FalloutNativePluginCampaignSelection? selection,
        FalloutNativePluginDataBindings? dataBindings = null)
    {
        _records = records; _source = records.OwnedSource ?? throw new NotSupportedException("Native campaign has no exact owned content source.");
        _quests = quests; _scripts = scripts; _world = scripts.References ?? throw new NotSupportedException("Native campaign has no actual reference world.");
        _selection = selection; _dataBindings = dataBindings;
        if (!ReferenceEquals(_world.NativeSourceRecords, records) || !ReferenceEquals(quests.NativeSourceRecords, records))
            throw new InvalidDataException("Native campaign world/quest/selected records differ.");
        var paths = _source.ResourcePathsUnder("NVSE/Plugins").Where(path =>
            Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
            path.Replace('/', '\\').Split('\\').Length == 3 && _source.TryResolve(path, null, out var winner) &&
            !winner.Contains("::", StringComparison.Ordinal)).ToArray();
        if (paths.Length == 0) return;
        if (selection is null || !ReferenceEquals(selection.Source, _source) || string.IsNullOrWhiteSpace(selection.LoadOrderOwner))
        {
            foreach (var logical in paths)
            {
                if (!_source.TryResolve(logical, null, out var original)) throw new InvalidDataException("Selected native module disappeared: " + logical);
                _failures.Add(new(logical, original, HashLoose(original), null, null, null, null,
                    "Selected original module has no exact runtime/edition/loader order/import/object declarations.", [], false));
            }
            return;
        }
        var selected = selection.Inventory.Where(row => row.Disposition == FalloutNativeModuleDisposition.SelectedPlugin).ToArray();
        var physicalCandidates = selection.Inventory.Where(row => row.Disposition is FalloutNativeModuleDisposition.SelectedPlugin or
            FalloutNativeModuleDisposition.LoaderNotPlugin or FalloutNativeModuleDisposition.SourceFailure).ToArray();
        if (physicalCandidates.Length != paths.Length || paths.Any(path => !physicalCandidates.Any(row => StringComparer.OrdinalIgnoreCase.Equals(row.LogicalPath, path))) ||
            selection.Modules.Count != selected.Length || selection.Modules.Select(row => row.LogicalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length ||
            selected.Any(row => !selection.Modules.Any(module => module.LogicalPath == row.LogicalPath && module.Sha256 == row.Sha256 && module.QueryHandle == row.QueryHandle)))
            throw new InvalidDataException("Native module declaration omits, duplicates or substitutes an actual source candidate/disposition.");
        foreach (var row in physicalCandidates)
        {
            if (!_source.TryResolve(row.LogicalPath, null, out var current) || !StringComparer.OrdinalIgnoreCase.Equals(current, row.PhysicalPath) ||
                !StringComparer.OrdinalIgnoreCase.Equals(HashLoose(current), row.Sha256))
                throw new InvalidDataException("Native source candidate changed after declaration: " + row.LogicalPath);
            if (row.Disposition == FalloutNativeModuleDisposition.SourceFailure)
                _failures.Add(new(row.LogicalPath, row.PhysicalPath, row.Sha256, null, null, null, null, row.Failure, [], false));
        }
        try
        {
            foreach (var admission in selection.Modules) AdmitQuery(admission, companion, privateStateRoot);
            foreach (var module in _modules.Where(module => module.Plugin.Phase == NativeNvsePhase.QueriedTrue))
                InitializeStage(module, "NVSE message 23", () => module.Domain.DeliverNvseMessage(module.Plugin, 23, []));
            uint loadHandle = 0;
            foreach (var module in _modules.Where(module => module.Plugin.Phase == NativeNvsePhase.QueriedTrue && module.ObjectFailure is null))
            {
                var actualHandle = checked(++loadHandle);
                InitializeStage(module, "NVSEPlugin_Load", () =>
                {
                    if (module.Admission.ExpressionAbi is { } expression)
                        module.Domain.ConfigureNvseExpressionAbi(module.Plugin, expression);
                    // An absent declaration leaves Init itself refused; clients
                    // that never request it do not get a fabricated empty table.
                    _ = module.Domain.InitializeNvse(module.Plugin, actualHandle); return 0;
                });
            }
            foreach (var message in new uint[] { 0, 9 })
                foreach (var module in _modules.Where(module => module.Plugin.Phase == NativeNvsePhase.LoadedTrue && module.ObjectFailure is null))
                    InitializeStage(module, $"NVSE message {message}", () => module.Domain.DeliverNvseMessage(module.Plugin, message, []));
            foreach (var module in _modules)
            {
                if (module.Plugin.Phase != NativeNvsePhase.LoadedTrue || module.ObjectFailure is not null || module.Domain.Fault is not null || module.Plugin.Registry.UnownedRequests.Count != 0) continue;
                foreach (var command in module.Plugin.Registry.Commands)
                {
                    if (command.AssignedOpcode > ushort.MaxValue || !_commands.TryAdd(checked((ushort)command.AssignedOpcode), (module, command)))
                        throw new InvalidDataException("Actual original command registrations conflict or exceed the compiled UInt16 opcode domain.");
                }
            }
        }
        catch (Exception error)
        {
            try { Dispose(); } catch (Exception retirement) { throw new FalloutNativePluginCampaignConstructionFault(error, retirement, this); }
            throw;
        }
        // Only source loader completion events are produced above. Main-loop,
        // gameplay, original cross-module hooks and co-save remain independent
        // authoritative event owners; Load success cannot stand in for them.
    }

    public IReadOnlyList<FalloutNativePluginCampaignModule> Modules => _failures.Concat(_modules.Select(module =>
        new FalloutNativePluginCampaignModule(module.Admission.LogicalPath, module.Admission.PhysicalPath, module.Admission.Sha256,
            module.Domain.Generation, module.Domain.ProcessId, module.Plugin.QueryReceipt?.Returned, module.Plugin.LoadReceipt?.Returned,
            module.ObjectFailure ?? (module.Domain.Fault is { } fault ?
                $"Native {fault.Operation}, code {fault.NativeCode?.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) ?? "unreported"}: {fault.Reason}" :
                module.Domain.NvseSourceFileDiagnostics), module.Plugin.Registry.UnownedRequests.Select(row => row.Operation).ToArray(), module.Retired))).ToArray();

    private void AdmitQuery(FalloutNativePluginModuleAdmission admission, string companion, string privateStateRoot)
    {
        if (!_source.TryResolve(admission.LogicalPath, null, out var physical) || physical.Contains("::", StringComparison.Ordinal) ||
            !Path.GetFullPath(physical).Equals(Path.GetFullPath(admission.PhysicalPath), StringComparison.OrdinalIgnoreCase) ||
            !StringComparer.OrdinalIgnoreCase.Equals(HashLoose(physical), admission.Sha256))
            throw new InvalidDataException("Native module declaration differs from the exact selected loose winner.");
        if (admission.QueryHandle is 0 or uint.MaxValue) throw new InvalidDataException("Selected original has no source Query handle rank.");
        if (admission.PreEntryFailure is { } preEntry)
        {
            _failures.Add(new(admission.LogicalPath, physical, admission.Sha256, null, null, null, null, preEntry, [], false)); return;
        }
        NativePluginExecutionDomain? domain = null; NativeNvseHostSource? host = null; NativePluginPrivateIo? io = null;
        NativeNvsePlugin? plugin = null;
        try
        {
            var selection = _selection!;
            host = NativeNvseHostSource.Open(_source.FalloutExecutablePath, selection.RuntimeSha256,
                selection.NvsePath, selection.NvseSha256, _source.StackId, selection.NoGore, selection.EditionOwner);
            io = FalloutNativePluginPrivateIo.Create(_source, physical, admission.Sha256, privateStateRoot,
                admission.WriteScopes, admission.ReadDeclarations, admission.InputRoots, admission.ImportOwner, admission.NonIoImports);
            domain = new(companion, privateIo: io); io = null;
            domain.BindCngSystemServiceBuild(companion);
            domain.BindNativeImportProviderBuild(companion);
            plugin = domain.LoadNvseImage(host, physical, admission.Sha256, sourcePluginHandle: admission.QueryHandle); host = null;
            domain.AttachNvseValues(plugin, new FalloutNativePluginValues(_scripts.ScriptValues, _records, domain, plugin));
            domain.AttachNvseScriptInterface(plugin);
            domain.AttachNvseCommandTable(plugin, new CampaignCommandTable(this));
            if (_dataBindings is { } data) domain.AttachNvseData(plugin, new CampaignData(this, domain, plugin, data));
            if (admission.Heap is { } heap) domain.ConfigureNvseValueHeap(plugin, heap);
            if (admission.SourceFiles is { } files) domain.ConfigureNvseSourceFileMethods(plugin, files);
            _ = domain.QueryNvse(plugin);
            _modules.Add(new(admission, domain, plugin)); domain = null;
        }
        catch (Exception error)
        {
            var failure = error.ToString();
            domain ??= (error as NativePluginDomainFaultException)?.Owner;
            try { domain?.Dispose(); } catch (Exception retirement) { failure += "\nNative retirement: " + retirement; }
            if (domain is not null && !domain.ChildExited) _retiringDomains.Add(domain);
            try { host?.Dispose(); } catch (Exception retirement) { failure += "\nSource retirement: " + retirement; }
            try { io?.Dispose(); }
            catch (Exception retirement) { if (io is not null) _retiringIos.Add(io); failure += "\nI/O retirement: " + retirement; }
            _failures.Add(new(admission.LogicalPath, physical, admission.Sha256, domain?.Generation, domain?.ProcessId,
                plugin?.QueryReceipt?.Returned, plugin?.LoadReceipt?.Returned, failure,
                plugin?.Registry.UnownedRequests.Select(row => row.Operation).ToArray() ?? [], domain?.NaturallyRetired ?? false));
        }
    }

    private void InitializeStage(Module module, string stage, Func<uint> action)
    {
        try { _ = action(); }
        catch (Exception error)
        {
            module.ObjectFailure = stage + ": " + error;
            // Preserve the original reached prefix and every callback. Never
            // release guest-callable owners until the exact child has exited.
            try { module.Domain.Dispose(); module.Retired = module.Domain.NaturallyRetired; }
            catch (Exception retirement) { module.ObjectFailure += "\nNative retirement: " + retirement; }
        }
    }
    internal IReadOnlyList<FalloutNativeModuleSource> SourceInventory => _selection?.Inventory ?? [];

    public bool ContainsOpcode(ushort opcode)
    {
        RequireCurrent(); return _commands.ContainsKey(opcode);
    }

    public FalloutCompiledCommandDeclaration? Declaration(ushort opcode)
    {
        RequireCurrent();
        if (!_commands.TryGetValue(opcode, out var declaration)) return null;
        var command = declaration.Command;
        if (!command.Name.IsAscii || command.Parameters.Any(parameter => parameter.Type > byte.MaxValue || parameter.Optional > 1))
            throw new NotSupportedException("Original command declaration needs its actual locale/parameter ABI owner.");
        var required = command.Parameters.TakeWhile(parameter => parameter.Optional == 0).Count();
        if (command.Parameters.Skip(required).Any(parameter => parameter.Optional == 0))
            throw new InvalidDataException("Original command optional parameter order is not a suffix.");
        // The source argument decoder, not the parse function address, admits
        // actual vanilla bytes. Compiler override streams remain unowned.
        return new(command.Name.Display, command.NeedsParent != 0, required,
            command.Parameters.Select(parameter => checked((byte)parameter.Type)).ToArray(), true);
    }

    public FalloutScriptValue Invoke(FalloutNativePluginSourceCall call)
    {
        RequireCurrent();
        if (!_commands.TryGetValue(call.Opcode, out var declared))
            throw new NotSupportedException($"Original command {call.Opcode:x4} has no genuinely loaded selected module: " +
                string.Join("; ", Modules.Where(row => row.Failure is not null || row.Load != true).Select(row => row.LogicalPath + ":" + row.Failure)));
        if (!call.Program.Standalone || !ReferenceEquals(_records.GetEffective(call.Program.Source.FormKey), call.Program.Source) ||
            call.Receiver is not null || declared.Command.NeedsParent != 0)
            throw new NotSupportedException("Original caller requires its embedded Script/actual native reference projection owner.");
        var module = declared.Module;
        var sourceObject = Object(module, call.Program.Source.FormKey);
        var code = sourceObject.Code ?? throw new InvalidDataException("Native caller has no original compiled body.");
        if (call.Start > call.End || call.End > code.Length) throw new InvalidDataException("Native caller argument range exceeds its actual SCDA.");
        if (!module.Locals.TryGetValue(call.Owner, out var locals))
        {
            var authority = FalloutNativePluginLocals.BindCampaign(_records, _quests, _world, _scripts.ScriptValues, call.Owner);
            locals = module.Domain.BindNvseLocalContext(module.Plugin, authority, sourceObject);
            module.Locals.Add(call.Owner, locals);
        }
        RefreshCampaignGraphs(module);
        var arguments = FalloutNativePluginExpressionStream.Bind(call, locals);
        // Form tokens can publish only the exact objects retained before this
        // call. No callback may instantiate a proxy or allocate a replacement.
        var reachable = new HashSet<NativeNvseSourceObject>(); var pending = new Stack<NativeNvseSourceObject>(module.Objects.Values);
        while (pending.TryPop(out var retained))
            if (reachable.Add(retained)) foreach (var dependency in retained.Dependencies) pending.Push(dependency);
        var graph = reachable.ToArray(); ++_active;
        try
        {
            var receipt = module.Domain.CallNvseSourceCommand(module.Plugin, declared.Command, code, arguments, locals,
                sourceObject, graph, call.ResultTarget);
            if (!receipt.Returned) throw new InvalidOperationException("Original native command returned false: " + declared.Command.Name.Display);
            if (receipt.TypedResult is not null) throw new NotSupportedException("Typed native return needs its genuine compiled assignment target publication.");
            return receipt.NumericResult;
        }
        finally { --_active; }
    }

    private NativeNvseSourceObject Object(Module module, FalloutFormKey key) => CampaignGraphObject(module, key);

    public void RequireIdleForSave()
    {
        RequireCurrent();
        if (ModuleFailure is { } failure) throw new NotSupportedException("Selected native module state is incomplete: " + failure);
        if (_active != 0) throw new InvalidOperationException("An original native caller still owns campaign state.");
        RequireDataIdleForSave();
        var source = CaptureSourceContinuation();
        if (source is not null) RequireSourceContinuationCurrent(source);
        foreach (var module in _modules)
        {
            module.Domain.RequireNvseSourceFilesSaveOwned(); module.Domain.RequireNvseBinarySaveOwned();
            module.Domain.RequirePrivateCrtSaveOwned(); module.Domain.RequirePrivateProfileDirectorySaveOwned();
            module.Domain.RequirePrivateCryptoMappingSaveOwned(); module.Domain.RequirePrivateMutexSaveOwned();
        }
        if (_modules.Any(module => module.Plugin.Registry.SerializationHistory.Count != 0))
            throw new NotSupportedException("Original plugin co-save callback state has no current unified campaign writer/reader owner.");
        RequireOriginalModuleColdContinuation();
    }
    private void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread || !ReferenceEquals(_records.OwnedSource, _source) ||
            _records.Plugins.Any(plugin => !plugin.Plugin.NativeSourceAvailable))
            throw new InvalidOperationException("Native campaign thread/source/record lifetime changed.");
    }
    public void Dispose()
    {
        if (_disposed && _graphSource is null && ChildDomainsExited && _retiringIos.Count == 0) return;
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Native campaign retirement changed owner thread.");
        if (_active != 0) throw new InvalidOperationException("Native campaign cannot retire during an actual original call.");
        _disposed = true; var errors = new List<Exception>();
        foreach (var module in _modules.AsEnumerable().Reverse())
        {
            try
            {
                // Domain retirement keeps native objects/locals/source leases
                // alive through actual FreeLibrary, then retires callbacks,
                // mappings, data capabilities and the exact child process.
                module.Domain.Dispose(); module.Retired = module.Domain.NaturallyRetired;
            }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                try { module.Domain.Dispose(); module.Retired = module.Domain.NaturallyRetired; }
                catch (Exception error) { errors.Add(error); }
            }
        }
        foreach (var domain in _retiringDomains)
            try { domain.Dispose(); } catch (Exception error) { errors.Add(error); }
        foreach (var io in _retiringIos.ToArray())
        {
            try { io.Dispose(); _retiringIos.Remove(io); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count == 0 && _graphSource is { } graph)
        {
            try
            {
                if (_modules.Any(module => !module.Domain.ChildExited || !module.Domain.NaturallyRetired) ||
                    _retiringDomains.Any(domain => !domain.ChildExited || !domain.NaturallyRetired))
                    throw new InvalidOperationException("Contributor source retirement requires verified native child closure.");
                graph.RetireLoadedContributors(); _graphSource = null;
            }
            catch (Exception error) { errors.Add(error); }
        }
        _commands.Clear(); if (errors.Count != 0) throw new AggregateException("Native campaign retirement retained failures.", errors);
    }
    private static string HashLoose(string path)
    {
        if (path.Contains("::", StringComparison.Ordinal)) throw new NotSupportedException("Original native image is not a selected loose Windows module.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
