using System.ComponentModel;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private readonly Dictionary<ulong, NativeEngineGlobal> _engineGlobals = [];
    private readonly List<NativeEngineGlobalEvent> _engineGlobalEvents = [];
    private readonly List<NativeEngineTlsEvent> _engineTlsEvents = [];
    private readonly List<IDisposable> _failedEngineGlobalSourceLeases = [];
    private NativeEngineStaticTls? _engineTls;
    private ulong _nextEngineGlobal;
    internal IReadOnlyList<NativeEngineGlobalEvent> EngineGlobalEvents => _engineGlobalEvents.AsReadOnly();
    internal IReadOnlyList<NativeEngineTlsEvent> EngineTlsEvents => _engineTlsEvents.AsReadOnly();

    // This source phase owns Windows loader initialization only. A current
    // interpreter/task/scene TLS image has its separate complete authority.
    internal NativeEngineStaticTls PrepareNvseEngineStaticTls(NativeNvsePlugin plugin, string companion)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall();
        if (_engineTls is not null) throw new InvalidOperationException("Static TLS preparation cannot replay an entered lifetime.");
        _process.RequireEngineTlsBuildProcess(companion);
        var host = EngineGlobalHost(); var source = NativePluginEngineTlsSource.Read(host, Generation);
        var initial = source.ReadLoaderInitialImage(); var build = NativePluginEngineTlsBuild.Read(companion);
        var tls = new NativeEngineStaticTls(Generation, plugin.Module, NativeThread, source, build.Path, build.Sha256)
        { PublishedImage = initial, CarrierLease = build.Lease, CarrierIndexRva = build.IndexRva };
        _engineTls = tls; ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.SourceGlobals, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(0U); WriteText(writer, tls.Carrier);
                writer.Write(checked((uint)initial.Length)); writer.Write(initial);
            }));
            var image = reader.ReadUInt32(); var index = reader.ReadUInt32(); var pointer = reader.ReadUInt32(); var extent = reader.ReadUInt32(); Finish(reader);
            if (!tls.Published || tls.Image != image || tls.Index != index || tls.Pointer != pointer || tls.Extent != extent || tls.Failure is not null)
                throw new InvalidDataException("Static TLS lacks its correlated Windows loader/source initialization receipt.");
            return tls;
        }
        catch (Exception failure) { tls.Failure ??= failure.Message; throw; }
        finally { --_callDepth; }
    }

    internal NativeEngineGlobal PublishNvseEngineTlsIndex(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall();
        var tls = RequireEngineTls(plugin);
        return PublishEngineGlobal(plugin, tls.Source.IndexAddress, NativeEngineGlobalKind.StaticTlsIndex,
            tls.Index, "selected PE32 static-TLS loader index", null, null, null);
    }

    internal NativeEngineGlobal PublishNvseEngineWord(NativeNvsePlugin plugin, NativeNvseEngineWordAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority); authority.RequireCurrent(); RequireEngineAuthorityIdentity(authority.RuntimeSha256, authority.SourceOwner);
        var value = authority.Read(); var lease = authority.Retain();
        return PublishEngineGlobal(plugin, authority.SourceAddress, NativeEngineGlobalKind.Word, value,
            authority.SourceOwner, authority, null, lease);
    }

    internal NativeEngineGlobal PublishNvseEngineObject(NativeNvsePlugin plugin, NativeNvseSourceObject value,
        NativeNvseEngineObjectAuthority authority)
    {
        VerifyNvseSourceObject(plugin, value); ArgumentNullException.ThrowIfNull(authority);
        RequireEngineAuthorityIdentity(authority.RuntimeSha256, authority.SourceOwner); authority.RequireBinding(value);
        if (value.Dependents == uint.MaxValue) throw new OverflowException("Engine global object retainers are exhausted.");
        var lease = authority.Retain();
        return PublishEngineGlobal(plugin, authority.SourceAddress, NativeEngineGlobalKind.Object, value.Address,
            authority.SourceOwner, null, (value, authority), lease);
    }

    private NativeEngineGlobal PublishEngineGlobal(NativeNvsePlugin plugin, uint address, NativeEngineGlobalKind kind,
        uint value, string owner, NativeNvseEngineWordAuthority? word,
        (NativeNvseSourceObject Object, NativeNvseEngineObjectAuthority Authority)? objectBinding, IDisposable? lease)
    {
        NativeEngineGlobal? global = null;
        try
        {
            VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); var host = EngineGlobalHost();
            NativePluginEngineTlsSource.RequireWritableCell(host, Generation, address);
            if (_engineGlobals.Values.Any(row => row.Address == address) || _engineGlobals.Count >= 4096)
                throw new InvalidOperationException("Engine global repeats a live/retired cell or exhausts its publication lifetime budget.");
            global = new(checked(++_nextEngineGlobal), Generation, plugin.Module, NativeThread, address, kind, value, owner)
            { Word = word, Object = objectBinding?.Object, ObjectAuthority = objectBinding?.Authority, SourceLease = lease };
            _engineGlobals.Add(global.Id, global);
            if (global.Object is { } target) ++target.Dependents;
            ++_callDepth;
            try
            {
                using var reader = Exchange(NativePluginDomainOperation.SourceGlobals, Payload(writer =>
                {
                    writer.Write(plugin.Module); writer.Write(1U); writer.Write(global.Id); writer.Write(address);
                    writer.Write((uint)kind); writer.Write(value); writer.Write(global.Object?.Id ?? 0UL);
                }));
                if (reader.ReadUInt64() != global.Id || reader.ReadUInt64() != global.Lease ||
                    global.Phase != NativeEngineGlobalPhase.Published || global.Failure is not null)
                    throw new InvalidDataException("Engine global lacks its actual source/native publication receipt.");
                Finish(reader); return global;
            }
            finally { --_callDepth; }
        }
        catch (Exception failure)
        {
            // An entered lifetime remains retained through child closure even
            // when no success receipt returned. Never free an exposed object.
            if (global is not null) global.Failure ??= failure.Message;
            else if (lease is not null)
            {
                try { lease.Dispose(); }
                catch (Exception closure)
                {
                    _failedEngineGlobalSourceLeases.Add(lease);
                    throw new AggregateException("Unentered engine-global source retainer failed independent closure.", failure, closure);
                }
            }
            throw;
        }
    }

    private uint DispatchNvseEngineGlobal(NativeNvseHostCall operation, Frame frame, ulong parent, BinaryReader reader)
    {
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Engine global callback lost its actual module.");
        var host = EngineGlobalHost();
        if (operation is NativeNvseHostCall.EngineTlsPublish or NativeNvseHostCall.EngineTlsRetire)
            return DispatchEngineTls(operation, frame, parent, reader, plugin);
        var id = reader.ReadUInt64(); var address = reader.ReadUInt32(); var kind = reader.ReadUInt32();
        var value = reader.ReadUInt32(); var lease = reader.ReadUInt64(); Finish(reader);
        if (!_engineGlobals.TryGetValue(id, out var global) || global.Generation != Generation || global.Module != plugin.Module ||
            global.Thread != NativeThread || global.Address != address || (uint)global.Kind != kind || global.Value != value || lease == 0)
            throw new InvalidDataException("Engine global callback escaped its exact source/module/thread/generation binding.");
        try
        {
            if (operation == NativeNvseHostCall.EngineGlobalPublish)
            {
                if (_operation != nameof(NativePluginDomainOperation.SourceGlobals) || global.Phase != NativeEngineGlobalPhase.Declared)
                    throw new InvalidDataException("Engine global publication repeated or left its genuine entered operation.");
                RequireEngineGlobalCurrent(global, plugin);
                if (_process.ReadEngineGlobal(address, host.SourceAddressSpace.ImageBase, host.SourceAddressSpace.ImageBytes) != value)
                    throw new InvalidDataException("Engine global native bytes differ from its complete authority.");
                global.Lease = lease; global.Phase = NativeEngineGlobalPhase.Published;
            }
            else if (operation == NativeNvseHostCall.EngineGlobalRetire)
            {
                if (_operation != nameof(NativePluginDomainOperation.UnloadNvse) || global.Phase != NativeEngineGlobalPhase.Published ||
                    global.Lease != lease || global.Calls != 0)
                    throw new InvalidDataException("Engine global retirement escaped its actual empty original unload.");
                if (_process.ReadEngineGlobal(address, host.SourceAddressSpace.ImageBase, host.SourceAddressSpace.ImageBytes) != 0)
                    throw new InvalidDataException("Retired engine global lacks its actual cleared native cell.");
                global.Phase = NativeEngineGlobalPhase.Retired;
            }
            else throw new InvalidDataException("Unknown engine global callback.");
            Record(); return 1;
        }
        catch (Exception failure) { global.Failure ??= failure.Message; Record(failure.Message); throw; }
        void Record(string? failure = null) => _engineGlobalEvents.Add(new(Generation, plugin.Module, NativeThread,
            parent, frame.Id, id, address, global.Kind, value, global.Phase, null, null, failure));
    }

    private uint DispatchEngineTls(NativeNvseHostCall operation, Frame frame, ulong parent, BinaryReader reader, NativeNvsePlugin plugin)
    {
        var tls = _engineTls ?? throw new InvalidDataException("Static TLS callback has no entered retained source owner.");
        if (tls.Generation != Generation || tls.Module != plugin.Module || tls.Thread != NativeThread)
            throw new InvalidDataException("Static TLS callback belongs to another module/thread/generation.");
        uint? sdkResult = null, sdkLast = null;
        uint result = 0, last = 0;
        try
        {
            var image = reader.ReadUInt32(); var index = reader.ReadUInt32(); var pointer = reader.ReadUInt32();
            if (operation == NativeNvseHostCall.EngineTlsPublish)
            {
                var extent = reader.ReadUInt32(); var sourceBytes = reader.ReadUInt32(); last = reader.ReadUInt32();
                var current = reader.ReadUInt32(); Finish(reader);
                if (current == 0) { sdkResult = image; sdkLast = last; }
                if (_operation != nameof(NativePluginDomainOperation.SourceGlobals) || current > 1 ||
                    (current == 0 ? tls.Published : !tls.Published || tls.PendingImage is null || tls.CurrentAuthority is null) || tls.Retired ||
                    pointer == 0 || image == 0 || index == uint.MaxValue || extent < sourceBytes || sourceBytes != tls.Source.InitialBytes)
                    throw new InvalidDataException("Static TLS callback lost its actual first Windows allocation/source prefix.");
                if (current == 1 && (image != tls.Image || index != tls.Index || pointer != tls.Pointer || extent != tls.Extent))
                    throw new InvalidDataException("Current compiler TLS relocated or replaced an already published allocation/index.");
                tls.Image = image; tls.Index = index; tls.Pointer = pointer; tls.Extent = extent;
                tls.Source.RequireAlignment(pointer);
                _process.RequireEngineTlsCarrier(image, tls.CarrierIndexRva, index, tls.CarrierLease!);
                var expected = current == 0 ? tls.PublishedImage : tls.PendingImage!;
                if (current == 1)
                {
                    tls.CurrentAuthority!.RequireCurrent(NativeThread);
                    if (!ReadCompleteCurrentEngineTls(tls, tls.CurrentAuthority).AsSpan().SequenceEqual(expected))
                        throw new InvalidDataException("Current TLS fields changed inside their native publication lease.");
                }
                if (!_process.ReadEngineStaticTls(pointer, sourceBytes).AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("Static TLS source initialization lacks complete actual thread-byte readback.");
                if (current == 1) { tls.PublishedImage = expected; tls.PendingImage = null; tls.SourceConstructed = true; }
                tls.Published = true;
            }
            else if (operation == NativeNvseHostCall.EngineTlsRetire)
            {
                result = reader.ReadUInt32(); last = reader.ReadUInt32(); var queried = reader.ReadUInt32(); var state = reader.ReadUInt32(); Finish(reader);
                sdkResult = result; sdkLast = last;
                if (_operation != nameof(NativePluginDomainOperation.UnloadNvse) || !tls.Published || tls.Retired ||
                    image != tls.Image || index != tls.Index || pointer != tls.Pointer)
                    throw new InvalidDataException("Static TLS retirement escaped its actual carrier/thread unload.");
                if (result == 0) throw new Win32Exception(unchecked((int)last), "Actual static TLS carrier FreeLibrary failed.");
                if (queried == 0 || state != 0x10000) throw new InvalidDataException("Actual static TLS image has no complete unload observation.");
                _process.RequireEngineTlsCarrierGone(image); tls.Retired = true;
            }
            else throw new InvalidDataException("Unknown compiler TLS callback.");
            Record(); return 1;
        }
        catch (Exception failure) { tls.Failure ??= failure.Message; Record(failure.Message); throw; }
        void Record(string? failure = null) => _engineTlsEvents.Add(new(Generation, plugin.Module, NativeThread,
            frame.Id, parent, tls.Image, tls.Index, tls.Pointer, tls.Extent, tls.Retired, tls.SourceConstructed, sdkResult, sdkLast, failure));
    }

    private NativeNvseHostSource EngineGlobalHost()
    {
        var host = _nvseHostSource ?? throw new InvalidDataException("Engine globals have no retained selected executable.");
        host.Check(Generation); RequireSourceAddressSpace(host); return host;
    }
    private void RequireEngineAuthorityIdentity(string runtime, string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || !StringComparer.OrdinalIgnoreCase.Equals(runtime, EngineGlobalHost().RuntimeSha256))
            throw new InvalidDataException("Engine global authority is not bound to this exact selected runtime.");
    }
    private NativeEngineStaticTls RequireEngineTls(NativeNvsePlugin plugin)
    {
        var tls = _engineTls ?? throw new NotSupportedException("Original compiler TLS has no genuine loader/current-thread owner.");
        if (!tls.Published || tls.Retired || tls.Failure is not null || tls.Generation != Generation || tls.Module != plugin.Module || tls.Thread != NativeThread)
            throw new InvalidOperationException("Original compiler TLS retains a failed/stale/unpublished lifetime.");
        return tls;
    }
    private void RequireEngineGlobalCurrent(NativeEngineGlobal global, NativeNvsePlugin plugin)
    {
        if (global.Failure is not null || global.Generation != Generation || global.Module != plugin.Module || global.Thread != NativeThread)
            throw new InvalidOperationException("Engine global has a failed or foreign current lifetime.");
        if (global.Word is { } word) { word.RequireCurrent(); if (word.Read() != global.Value) throw new NotSupportedException("Engine scalar changed without its actual native refresh owner."); }
        else if (global.Object is { } value)
        { VerifyNvseSourceObject(plugin, value); global.ObjectAuthority!.RequireBinding(value); if (value.Address != global.Value) throw new InvalidDataException("Engine global object pointer changed its retained identity."); }
        else if (global.Kind != NativeEngineGlobalKind.StaticTlsIndex || RequireEngineTls(plugin).Index != global.Value)
            throw new NotSupportedException("Engine global has no actual complete scalar/object/static-TLS authority.");
    }
    private void RequireEngineGlobalsRetired()
    {
        if (_engineGlobals.Values.Any(value => value.Phase != NativeEngineGlobalPhase.Retired || value.Failure is not null) ||
            _engineTls is { Retired: false } || _engineTls?.Failure is not null)
            throw new InvalidDataException("Original retirement retains a failed/live engine global or static TLS carrier.");
    }
    private void ClearEngineGlobalsAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Engine global/source/carrier leases require exact child closure.");
        List<Exception> failures = [];
        foreach (var global in _engineGlobals.Values)
        {
            if (global.Phase == NativeEngineGlobalPhase.Closed) continue;
            try
            {
                global.SourceLease?.Dispose(); global.SourceLease = null;
                if (global.Object is { } value)
                { if (value.Dependents == 0) throw new InvalidDataException("Engine global lost its real object retainer."); --value.Dependents; }
                global.Phase = NativeEngineGlobalPhase.Closed;
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (_engineTls is { } tls)
        {
            try { tls.CurrentLease?.Dispose(); tls.CurrentLease = null; } catch (Exception failure) { failures.Add(failure); }
            try { tls.CarrierLease?.Dispose(); tls.CarrierLease = null; } catch (Exception failure) { failures.Add(failure); }
        }
        foreach (var lease in _failedEngineGlobalSourceLeases.ToArray())
        {
            try { lease.Dispose(); _failedEngineGlobalSourceLeases.Remove(lease); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException("Engine global source leases retain independent closure failures.", failures);
    }
}
