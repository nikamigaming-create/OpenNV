using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private FalloutEngineCallSiteSource? _sourceCallSiteSource;
    private readonly Dictionary<uint, NativeSourceCallSite> _sourceCallSites = [];
    private readonly List<NativeSourceCallSiteEvent> _sourceCallSiteEvents = [];
    private uint _nextSourceCallSite;
    private NativeSourceCallSiteInvocation? _sourceCallSiteInvocation;
    internal IReadOnlyList<NativeSourceCallSiteEvent> SourceCallSiteEvents => _sourceCallSiteEvents.AsReadOnly();

    private uint DispatchNvseCallSite(NativeNvseHostCall operation, Frame frame, ulong parent, BinaryReader reader)
    {
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Source CALL lost its actual original module.");
        var host = _nvseHostSource ?? throw new InvalidDataException("Source CALL lost its selected executable lease.");
        host.Check(Generation); RequireSourceAddressSpace(host);
        NativeSourceCallSite? site = null; uint address = 0, action = 0;
        uint? sdkResult = null, last = null, old = null, pointerResult = null; ulong? receiverId = null;
        uint? requestedAddress = null, requestedExtent = null, requestedProtection = null, cohortAddress = null, cohortExtent = null;
        try
        {
            if (operation == NativeNvseHostCall.SourceCallSiteDeclare)
            {
                address = reader.ReadUInt32(); var extent = reader.ReadUInt32(); var getter = reader.ReadUInt32();
                if (extent != 5 || getter > 1 || plugin.Phase is not (NativeNvsePhase.Loading or NativeNvsePhase.LoadedTrue) ||
                    _operation is not (nameof(NativePluginDomainOperation.NvseLoad) or nameof(NativePluginDomainOperation.NvseMessage) or
                        nameof(NativePluginDomainOperation.SourceCallSites)))
                    throw new NotSupportedException("Source code mutation requires its entered loader/message or genuine typed call-site owner.");
                if (_sourceCallSites.TryGetValue(address, out site))
                {
                    RequireSourceCallSiteIdentity(site, plugin);
                    if ((site.Source.Getter is not null) != (getter == 1))
                        throw new NotSupportedException("Code-site transport cannot acquire an invented getter execution owner.");
                    if (site.Failure is not null || site.Phase is NativeSourceCallSitePhase.Retired or NativeSourceCallSitePhase.Closed)
                        throw new InvalidDataException("Source CALL cannot replay a failed or retired site.");
                    Finish(reader); Record(); return site.Id;
                }
                if (_nextSourceCallSite >= 256) throw new NotSupportedException("Native source CALL lifetime quota is exhausted.");
                _sourceCallSiteSource ??= FalloutExecutableStringTable.ReadEngineCallSiteSource(host.RuntimePath, host.RuntimeSha256);
                var declaration = getter == 1 ? _sourceCallSiteSource.ReadGetter(address) : _sourceCallSiteSource.Read(address);
                if ((ulong)address + (getter == 1 ? 6U : 5U) > (ulong)host.SourceAddressSpace.ImageBase + host.SourceAddressSpace.ImageBytes)
                    throw new InvalidDataException("First-party CALL/return producer leaves the selected source image.");
                site = new(checked(++_nextSourceCallSite), Generation, plugin.Module, NativeThread, declaration);
                _sourceCallSites.Add(address, site); Finish(reader); Record(); return site.Id;
            }
            var id = reader.ReadUInt32(); address = reader.ReadUInt32();
            if (!_sourceCallSites.TryGetValue(address, out site) || site.Id != id)
                throw new InvalidDataException("Source CALL callback has no actual retained declaration.");
            RequireSourceCallSiteIdentity(site, plugin);
            if (site.Failure is not null) throw new InvalidOperationException("Source CALL retains its failed entered prefix: " + site.Failure);
            if (operation == NativeNvseHostCall.SourceCallSitePublish)
            {
                var lease = reader.ReadUInt64(); var target = reader.ReadUInt32();
                if (site.Phase != NativeSourceCallSitePhase.Declared || lease == 0 || (target != 0) != (site.Source.Getter is not null))
                    throw new InvalidDataException("Source CALL repeated or omitted its first-party publication.");
                ReadProtectionCohort(reader, site);
                var initial = ReadSourceCallSiteCode(site, plugin, target, site.Protection);
                if (initial.Target != target || initial.Kind != (target == 0 ? NativeSourceCallSiteCodeKind.Unbound : NativeSourceCallSiteCodeKind.Call))
                    throw new InvalidDataException("Source CALL lacks exact native first-party bytes/target readback.");
                site.Lease = lease; site.DefaultTarget = site.CurrentTarget = target;
                site.Code = initial;
                site.Phase = NativeSourceCallSitePhase.Published; Finish(reader); Record(); return 1;
            }
            if (operation == NativeNvseHostCall.SourceCallSiteTransition)
            {
                action = reader.ReadUInt32(); var requested = reader.ReadUInt32();
                sdkResult = reader.ReadUInt32(); last = reader.ReadUInt32(); var observedOld = reader.ReadUInt32();
                requestedAddress = reader.ReadUInt32(); requestedExtent = reader.ReadUInt32(); requestedProtection = requested;
                var prior = site.Protection; ReadProtectionCohort(reader, site);
                var actualProtection = reader.ReadUInt32();
                Finish(reader);
                if (requestedExtent != 5 || actualProtection != site.Protection || site.Calls != 0)
                    throw new InvalidDataException("Source SDK receipt lost its exact request/protection/calling prefix.");
                var successful = sdkResult != 0;
                old = successful && action != 3 ? observedOld : null;
                if (action == 1)
                {
                    if (requestedAddress != address || requested != 0x40 || successful && (observedOld != prior || actualProtection != requested) ||
                        site.Phase is not (NativeSourceCallSitePhase.Published or NativeSourceCallSitePhase.Flushed or NativeSourceCallSitePhase.Writable or NativeSourceCallSitePhase.Restored))
                        throw new InvalidDataException("Source CALL writable transition escaped its actual source/publication prefix.");
                    if (successful)
                    {
                        site.PreviousProtection = observedOld; site.CachePublished = false; site.Phase = NativeSourceCallSitePhase.Writable;
                    }
                }
                else if (action == 2)
                {
                    if (requestedAddress != address || requested != site.PreviousProtection || requested != 0x20 ||
                        successful && (observedOld != prior || actualProtection != requested) ||
                        site.Phase is not (NativeSourceCallSitePhase.Writable or NativeSourceCallSitePhase.Flushed or NativeSourceCallSitePhase.Restored))
                        throw new InvalidDataException("Source CALL protection did not restore its actual prior executable access.");
                    if (successful)
                    {
                        ReadMutation(); site.Phase = site.CachePublished ? NativeSourceCallSitePhase.Flushed : NativeSourceCallSitePhase.Restored;
                    }
                }
                else if (action == 3)
                {
                    if (requestedAddress != address || requested != 0 ||
                        site.Phase is not (NativeSourceCallSitePhase.Writable or NativeSourceCallSitePhase.Restored or NativeSourceCallSitePhase.Flushed))
                        throw new InvalidDataException("Source CALL has no exact entered instruction-cache publication prefix.");
                    ReadMutation();
                    if (successful) { site.CachePublished = true; site.Phase = NativeSourceCallSitePhase.Flushed; }
                }
                else if (action == 4)
                {
                    if (successful || requestedAddress == address || requested != site.PreviousProtection ||
                        site.Phase != NativeSourceCallSitePhase.Writable || cohortAddress != 0 || cohortExtent != 0)
                        throw new NotSupportedException("Foreign protection attempt has no failed SDK result bound to this entered source mutation.");
                    // BOOL false is a genuine API result. No restoration, target
                    // execution or source failure is manufactured from it.
                    _ = ReadSourceCallSiteCode(site, plugin, site.DefaultTarget, site.Protection);
                }
                else throw new InvalidDataException("Source CALL SDK transition has an unowned action.");
                Record();
                if (!successful) site.SdkFailureDiagnosticStart = _sourceCallSiteEvents.Count - 1;
                if ((!successful || action == 3) && site.SdkFailureDiagnosticStart is { } diagnosticStart)
                    RetainSourceCallSiteFailure(diagnosticStart);
                if (action == 3 && successful) site.SdkFailureDiagnosticStart = null;
                return 1;
                void ReadMutation()
                {
                    var code = ReadSourceCallSiteCode(site, plugin, site.DefaultTarget, site.Protection);
                    if (code.Kind is not (NativeSourceCallSiteCodeKind.Call or NativeSourceCallSiteCodeKind.Suppressed))
                        throw new NotSupportedException("Code site has no source CALL or suppression publication.");
                    if (site.CachePublished && code != site.Code)
                        throw new InvalidDataException("Source code changed outside its recorded instruction-cache publication.");
                    if (code != site.Code) site.Mutations = checked(site.Mutations + 1);
                    site.Code = code; site.CurrentTarget = code.Target;
                }
            }
            if (operation == NativeNvseHostCall.SourceCallSiteExecute)
            {
                var nativeReceiver = reader.ReadUInt32(); Finish(reader);
                var invocation = _sourceCallSiteInvocation ?? throw new NotSupportedException("Source CALL has no genuine C# object invocation lease.");
                receiverId = invocation.Receiver.Id;
                if (!ReferenceEquals(invocation.Site, site) || invocation.Receiver.Address != nativeReceiver || site.Calls != 1 ||
                    site.Replacement || site.Suppressed || site.Source.Getter is null || site.Phase is not (NativeSourceCallSitePhase.Published or NativeSourceCallSitePhase.Flushed))
                    throw new InvalidDataException("Pure getter callback lost its bound object/source/first-party target prefix.");
                var current = ReadSourceGetterPointer(plugin, site, invocation.Receiver);
                if (current.Pointer != invocation.ExpectedPointer || !ReferenceEquals(current.Owner, invocation.PointerOwner))
                    throw new InvalidDataException("Authoritative pointer changed inside the actual typed getter call.");
                pointerResult = current.Pointer; Record(); return current.Pointer;
            }
            if (operation == NativeNvseHostCall.SourceCallSiteRetire)
            {
                var lease = reader.ReadUInt64();
                if (_operation != nameof(NativePluginDomainOperation.UnloadNvse) || lease != site.Lease || site.Calls != 0 ||
                    site.Phase is NativeSourceCallSitePhase.Declared or NativeSourceCallSitePhase.Writable or NativeSourceCallSitePhase.Retired or NativeSourceCallSitePhase.Closed)
                    throw new InvalidDataException("Source CALL retirement escaped its real empty original-module unload.");
                ReadProtectionCohort(reader, site); Finish(reader);
                site.Code = _process.ReadSourceCallSite(address, host.SourceAddressSpace.ImageBase, host.SourceAddressSpace.ImageBytes,
                    site.DefaultTarget, plugin.Image, site.Protection, site.Source.Getter is not null, retired: true);
                site.CurrentTarget = 0;
                site.Phase = NativeSourceCallSitePhase.Retired; Record(); return 1;
            }
            throw new InvalidDataException("Unknown source CALL callback operation.");
        }
        catch (Exception failure)
        {
            if (site is not null) site.Failure ??= failure.Message;
            Record(failure.Message);
            try { RetainSourceCallSiteFailure(); }
            catch (Exception delivery)
            {
                throw new AggregateException("Source CALL failed and private diagnostic publication failed.", failure, delivery);
            }
            throw;
        }
        void Record(string? failure = null) => _sourceCallSiteEvents.Add(new(Generation, plugin.Module, NativeThread,
            frame.Id, parent, operation, site?.Id ?? 0, address, host.RuntimeSha256,
            site?.Phase ?? NativeSourceCallSitePhase.Declared, site?.CurrentTarget ?? 0,
            action, sdkResult, last, old, receiverId, pointerResult, failure, site?.Code?.Kind, site?.Code?.Sha256,
            requestedAddress, requestedExtent, requestedProtection, site?.Protection, cohortAddress, cohortExtent));
        void ReadProtectionCohort(BinaryReader input, NativeSourceCallSite active)
        {
            cohortAddress = input.ReadUInt32(); cohortExtent = input.ReadUInt32(); var protection = input.ReadUInt32();
            if (cohortExtent == 0)
            {
                if (cohortAddress != 0 || protection != 0) throw new InvalidDataException("Absent source SDK cohort retained a fabricated page transition.");
                return;
            }
            var reservation = SourceAddressSpaceReceipt ?? throw new InvalidDataException("Source protection lost its actual reservation receipt.");
            var end = (ulong)cohortAddress.Value + cohortExtent.Value;
            if (protection is not (0x20 or 0x40) || cohortAddress < reservation.ImageBase ||
                end > (ulong)reservation.ImageBase + reservation.ReservedBytes ||
                active.Source.Address < cohortAddress || (ulong)active.Source.Address + (uint)active.Extent > end)
                throw new InvalidDataException("Source protection cohort escaped its actual committed code-site extent.");
            foreach (var retained in _sourceCallSites.Values)
            {
                if ((ulong)retained.Source.Address >= end || (ulong)retained.Source.Address + (uint)retained.Extent <= cohortAddress.Value) continue;
                retained.Protection = protection;
                if (!ReferenceEquals(retained, active) && ReadSourceCallSiteCode(retained, plugin, retained.DefaultTarget, protection) != retained.Code)
                    throw new InvalidDataException("Another source code site changed inside the SDK page cohort.");
            }
        }
    }

    private void RequireSourceCallSiteIdentity(NativeSourceCallSite site, NativeNvsePlugin plugin)
    {
        if (site.Generation != Generation || site.Module != plugin.Module || site.Thread != NativeThread ||
            !_sourceCallSites.TryGetValue(site.Source.Address, out var retained) || !ReferenceEquals(site, retained))
            throw new InvalidDataException("Source CALL belongs to another module/thread/generation declaration.");
    }
    private NativeSourceCallSiteCode ReadSourceCallSiteCode(NativeSourceCallSite site, NativeNvsePlugin plugin, uint target, uint protection)
    {
        var host = _nvseHostSource ?? throw new InvalidDataException("Source CALL has no host lease.");
        return _process.ReadSourceCallSite(site.Source.Address, host.SourceAddressSpace.ImageBase,
            host.SourceAddressSpace.ImageBytes, target, plugin.Image, protection, site.Source.Getter is not null,
            retired: site.Phase == NativeSourceCallSitePhase.Retired, validateTarget: _operation != nameof(NativePluginDomainOperation.UnloadNvse));
    }
    private void ClearSourceCallSitesAfterChildExit()
    {
        if (!ChildExited || _sourceCallSiteInvocation is not null)
            throw new InvalidOperationException("Source CALL source/object owners require exact child closure and returned invocation.");
        foreach (var site in _sourceCallSites.Values) site.Phase = NativeSourceCallSitePhase.Closed;
        _sourceCallSiteSource = null;
    }
    private void RequireSourceCallSiteRetirement()
    {
        if (_sourceCallSites.Values.Any(site => site.Phase != NativeSourceCallSitePhase.Retired || site.Failure is not null))
            throw new InvalidDataException("Original-module retirement retains a live/failed source CALL site.");
    }
}
