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
    private readonly List<Module> _modules = [];
    private readonly List<NativePluginExecutionDomain> _retiringDomains = [];
    private readonly List<FalloutNativePluginCampaignModule> _failures = [];
    private readonly Dictionary<ushort, (Module Module, NativeNvseCommand Command)> _commands = [];
    private bool _disposed;
    private int _active;
    internal bool ChildDomainsExited => _modules.All(module => module.Domain.ChildExited) &&
        _retiringDomains.All(domain => domain.ChildExited);
    internal string? ModuleFailure => string.Join("; ", Modules.Where(module => module.Query != true || module.Load != true ||
        module.Failure is not null || module.Unowned.Count != 0).Select(module => module.LogicalPath + ": Query=" + module.Query +
            ", Load=" + module.Load + "; " + (module.Failure ?? string.Join(", ", module.Unowned)))) is { Length: > 0 } failure ? failure : null;

    internal FalloutNativePluginCampaign(FalloutPluginStack records, FalloutQuestState quests,
        FalloutQuestScripts scripts, string companion, string privateStateRoot, FalloutNativePluginCampaignSelection? selection)
    {
        _records = records; _source = records.OwnedSource ?? throw new NotSupportedException("Native campaign has no exact owned content source.");
        _quests = quests; _scripts = scripts; _world = scripts.References ?? throw new NotSupportedException("Native campaign has no actual reference world.");
        _selection = selection;
        if (!ReferenceEquals(_world.NativeSourceRecords, records) || !ReferenceEquals(quests.NativeSourceRecords, records))
            throw new InvalidDataException("Native campaign world/quest/selected records differ.");
        var paths = _source.ResourcePathsUnder("NVSE/Plugins").Where(path => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
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
        if (selection.Modules.Count != paths.Length || selection.Modules.Select(row => row.LogicalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length ||
            paths.Any(path => !selection.Modules.Any(row => StringComparer.OrdinalIgnoreCase.Equals(row.LogicalPath, path))))
            throw new InvalidDataException("Native module admission omits, duplicates or adds a selected source winner.");
        try
        {
            foreach (var admission in selection.Modules) Admit(admission, companion, privateStateRoot);
            foreach (var module in _modules)
            {
                if (module.Plugin.Phase != NativeNvsePhase.LoadedTrue || module.Plugin.Registry.UnownedRequests.Count != 0) continue;
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
        // PostLoad/PostPostLoad, game-loop and co-save notifications require
        // actual cross-module/native lifecycle producers. Successful Load alone
        // does not manufacture any of those events.
    }

    public IReadOnlyList<FalloutNativePluginCampaignModule> Modules => _failures.Concat(_modules.Select(module =>
        new FalloutNativePluginCampaignModule(module.Admission.LogicalPath, module.Admission.PhysicalPath, module.Admission.Sha256,
            module.Domain.Generation, module.Domain.ProcessId, module.Plugin.QueryReceipt?.Returned, module.Plugin.LoadReceipt?.Returned,
            module.Domain.Fault?.Reason ?? module.ObjectFailure ?? module.Domain.NvseSourceFileDiagnostics, module.Plugin.Registry.UnownedRequests.Select(row => row.Operation).ToArray(), module.Retired))).ToArray();

    private void Admit(FalloutNativePluginModuleAdmission admission, string companion, string privateStateRoot)
    {
        if (!_source.TryResolve(admission.LogicalPath, null, out var physical) || physical.Contains("::", StringComparison.Ordinal) ||
            !Path.GetFullPath(physical).Equals(Path.GetFullPath(admission.PhysicalPath), StringComparison.OrdinalIgnoreCase) ||
            !StringComparer.OrdinalIgnoreCase.Equals(HashLoose(physical), admission.Sha256))
            throw new InvalidDataException("Native module declaration differs from the exact selected loose winner.");
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
            plugin = domain.LoadNvseImage(host, physical, admission.Sha256); host = null;
            domain.AttachNvseValues(plugin, new FalloutNativePluginValues(_scripts.ScriptValues));
            domain.AttachNvseScriptInterface(plugin);
            if (admission.Heap is { } heap) domain.ConfigureNvseValueHeap(plugin, heap);
            if (admission.SourceFiles is { } files) domain.ConfigureNvseSourceFileMethods(plugin, files);
            var query = domain.QueryNvse(plugin);
            if (query.Returned)
            {
                domain.ConfigureNvseExpressionAbi(plugin, admission.ExpressionAbi);
                _ = domain.InitializeNvse(plugin);
            }
            _modules.Add(new(admission, domain, plugin)); domain = null;
        }
        catch (Exception error)
        {
            var failure = error.ToString();
            domain ??= (error as NativePluginDomainFaultException)?.Owner;
            try { domain?.Dispose(); } catch (Exception retirement) { failure += "\nNative retirement: " + retirement; }
            if (domain is not null && !domain.ChildExited) _retiringDomains.Add(domain);
            try { host?.Dispose(); } catch (Exception retirement) { failure += "\nSource retirement: " + retirement; }
            try { io?.Dispose(); } catch (Exception retirement) { failure += "\nI/O retirement: " + retirement; }
            _failures.Add(new(admission.LogicalPath, physical, admission.Sha256, domain?.Generation, domain?.ProcessId,
                plugin?.QueryReceipt?.Returned, plugin?.LoadReceipt?.Returned, failure,
                plugin?.Registry.UnownedRequests.Select(row => row.Operation).ToArray() ?? [], domain?.NaturallyRetired ?? false));
        }
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
        var arguments = FalloutNativePluginExpressionArguments.Deferred(call.Program.Scope.ScopeSha256 + ":" + call.Start + ":" + call.End,
            call.Start, call.End, call.Evaluate);
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
        foreach (var module in _modules) module.Domain.RequireNvseSourceFilesSaveOwned();
        if (_modules.Any(module => module.Plugin.Registry.SerializationHistory.Count != 0))
            throw new NotSupportedException("Original plugin co-save callback state has no current unified campaign writer/reader owner.");
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
        if (_disposed && _graphSource is null && _modules.All(module => module.Domain.ChildExited) && _retiringDomains.All(domain => domain.ChildExited)) return;
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
