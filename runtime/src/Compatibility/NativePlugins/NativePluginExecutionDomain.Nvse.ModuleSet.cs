namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private readonly Dictionary<ulong, NativeNvsePlugin> _nvseModules = [];
    private readonly Dictionary<ulong, FileStream> _nvseModuleSources = [];
    private readonly Dictionary<ulong, NativePluginImageDeclaration> _nvseModuleDeclarations = [];
    private readonly List<FileStream> _nvseFailedModuleSources = [];
    private uint _nvseLiveModules;
    private NativeNvsePlugin? _primaryNvseModule;
    internal IReadOnlyList<NativeNvsePlugin> NvseModules => _nvseModules.Values.ToArray();

    private void RegisterNvseModule(NativeNvsePlugin plugin, FileStream source, NativePluginImageDeclaration declaration)
    {
        if (!_nvseModules.TryAdd(plugin.Module, plugin) || !_nvseModuleSources.TryAdd(plugin.Module, source) ||
            !_nvseModuleDeclarations.TryAdd(plugin.Module, declaration))
            throw new InvalidDataException("Original module publication repeats its actual process/module identity.");
        _primaryNvseModule ??= plugin; _nvsePlugin = plugin;
        ++_nvseLiveModules;
    }
    internal NativeNvsePlugin LoadCoResidentNvseImage(string originalDll, string expectedSha256, uint sourcePluginHandle,
        NativePluginModuleReadIoSource? moduleRead = null)
    {
        VerifyOwner(); RequireNvseEmptyCall();
        if (_nvseLiveModules == 0 || _module is not null || _nvseHostSource is null || sourcePluginHandle is 0 or uint.MaxValue ||
            _nvseModules.Values.Any(row => row.Handle == sourcePluginHandle))
            throw new InvalidOperationException("Co-resident loading needs a genuine existing host and a distinct source Query handle.");
        _nvseHostSource.Check(Generation);
        var path = Path.GetFullPath(originalDll);
        if (_nvseModules.Values.Any(row => StringComparer.OrdinalIgnoreCase.Equals(row.Path, path)))
            throw new NotSupportedException("Original module hot reload requires its genuine source reload/global lifetime; retry uses a fresh process generation.");
        var io = _privateIo ?? throw new NotSupportedException("Co-resident loading lost its restricted process source roots.");
        if (!io.Selection.OriginalRoots.Any(root => NativePluginPrivateIo.Within(root, NativePluginPrivateIo.Canonical(path))))
            throw new NotSupportedException("Additional original DLL is outside the actual process's declared read-only source roots.");
        var source = NativeNvseHostSource.Lease(path, expectedSha256, dll: true);
        var entered = false;
        try
        {
            NativeNvseHostSource.RequirePluginExports(source);
            var declaration = NativePluginImageDeclarations.Read(path, dll: true);
            if (!declaration.Sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Co-resident source declaration changed after its retained file lease.");
            moduleRead ??= NativePluginModuleReadIoSource.Read(declaration, io.Selection.StackSha256);
            moduleRead?.Check(declaration, io.Selection.StackSha256);
            NativePluginCoResidentImports.Require(declaration, moduleRead);
            entered = true; ++_callDepth;
            using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer =>
            {
                writer.Write(0U); WriteText(writer, path); writer.Write(sourcePluginHandle);
                writer.Write(checked((uint)declaration.Imports.Count));
                foreach (var item in declaration.Imports)
                { WriteText(writer, item.Library.ToLowerInvariant()); WriteText(writer, item.Name!); writer.Write(item.SlotRva); }
            }));
            var id = reader.ReadUInt64(); var image = reader.ReadUInt32(); var handle = reader.ReadUInt32();
            var interfaces = reader.ReadUInt32(); var query = reader.ReadUInt32(); var load = reader.ReadUInt32();
            var boundReads = reader.ReadUInt32(); Finish(reader);
            if (id == 0 || image == 0 || interfaces == 0 || query == 0 || load == 0 || handle != sourcePluginHandle ||
                interfaces != _primaryNvseModule!.Interface || boundReads != (moduleRead?.BoundImports ?? 0))
                throw new InvalidDataException("Co-resident module lost its actual shared interface/image/export publication.");
            var plugin = new NativeNvsePlugin(Generation, id, handle, image, interfaces, query, load, path, expectedSha256.ToUpperInvariant());
            RegisterNvseModule(plugin, source, declaration); source = null!;
            if (moduleRead is not null) _moduleReadSources.Add(plugin.Module, moduleRead);
            return plugin;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception failure) { if (entered) throw Fatal(failure); throw; }
        finally
        {
            if (entered) --_callDepth;
            if (source is not null)
            {
                if (entered && Fault is not null && !ChildExited) _nvseFailedModuleSources.Add(source);
                else source.Dispose();
            }
        }
    }
    private NativeNvsePlugin RetainedNvseModule(ulong id)
    {
        if (!_nvseModules.TryGetValue(id, out var plugin) || plugin.Generation != Generation || plugin.Phase is NativeNvsePhase.Retired or NativeNvsePhase.Faulted)
            throw new InvalidDataException("Native callback has no current retained module in this process generation.");
        return plugin;
    }
    private bool IsSupplementalNvseModule(NativeNvsePlugin plugin) => !ReferenceEquals(plugin, _primaryNvseModule);
    private void RequireModulePrivateIo(NativeNvsePlugin plugin)
    {
        if (IsSupplementalNvseModule(plugin))
            throw new NotSupportedException("Additional original DLL requires its own module-bound private I/O producer; the primary policy cannot substitute.");
    }
    private void RequireModuleCoreCallback(Frame frame)
    {
        if (_nvsePlugin is not { } plugin || !IsSupplementalNvseModule(plugin)) return;
        if (frame.Kind == NativePluginDomainMessage.IoCallback)
        { if (frame.Operation is not (FileSizeCallback or ModuleReadCallback)) RequireModulePrivateIo(plugin); return; }
        if (frame.Kind != NativePluginDomainMessage.NvseCallback) return;
        if (frame.Operation != CommandTableEvent && (NativeNvseHostCall)frame.Operation is not
            (NativeNvseHostCall.QueryInterface or NativeNvseHostCall.SetOpcode or NativeNvseHostCall.RegisterCommand or
             NativeNvseHostCall.RegisterListener or NativeNvseHostCall.SerializationCallback or NativeNvseHostCall.Unsupported or
             NativeNvseHostCall.DispatchMessage or NativeNvseHostCall.DeliveredMessage or
             NativeNvseHostCall.BeginSerialization or NativeNvseHostCall.DeliveredSerialization))
            throw new NotSupportedException("Additional original module requires its own current engine object/service callback owner.");
    }
    private NativeNvsePlugin? SelectNvseCallbackModule(Frame frame)
    {
        var previous = _nvsePlugin;
        if (frame.Kind is not (NativePluginDomainMessage.NvseCallback or NativePluginDomainMessage.IoCallback) ||
            frame.Operation is SteamCallbackEvent or SteamSourceHashEvent) return previous;
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64();
        if (thread != NativeThread) throw new InvalidDataException("Module-set callback escaped its actual native thread.");
        if (_nvseModules.TryGetValue(module, out var plugin)) _nvsePlugin = RetainedNvseModule(plugin.Module);
        else if (frame.Kind != NativePluginDomainMessage.IoCallback || !IsEnteredOriginalLoaderIo(frame.Operation, module, frame.Parent))
            throw new InvalidDataException("Shared native callback has no actual registered original image owner.");
        RequireModuleCoreCallback(frame); return previous;
    }
    private void RetireNvseModuleSourcesAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Original module leases remain until their exact child exits.");
        var failures = new List<Exception>();
        foreach (var plugin in _nvseModules.Values)
        {
            plugin.Registry.Retire();
            if (plugin.Phase != NativeNvsePhase.Retired) plugin.Phase = NativeNvsePhase.Faulted;
        }
        foreach (var pair in _nvseModuleSources.ToArray())
            try { pair.Value.Dispose(); _nvseModuleSources.Remove(pair.Key); } catch (Exception failure) { failures.Add(failure); }
        foreach (var source in _nvseFailedModuleSources.ToArray())
            try { source.Dispose(); _nvseFailedModuleSources.Remove(source); } catch (Exception failure) { failures.Add(failure); }
        _nvseLiveModules = 0; _nvsePlugin = null;
        foreach (var function in _nvseFunctionBorrows.Values) function.Live = false;
        _nvseFunctionBorrows.Clear();
        if (failures.Count != 0) throw new AggregateException("Original module source leases did not all retire.", failures);
    }
}
