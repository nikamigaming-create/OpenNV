using System.ComponentModel;
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
        try
        {
            if (operation == NativeNvseHostCall.SourceCallSiteDeclare)
            {
                address = reader.ReadUInt32(); var extent = reader.ReadUInt32();
                if (extent != 5 || plugin.Phase is not (NativeNvsePhase.Loading or NativeNvsePhase.LoadedTrue) ||
                    _operation is not (nameof(NativePluginDomainOperation.NvseLoad) or nameof(NativePluginDomainOperation.NvseMessage) or
                        nameof(NativePluginDomainOperation.SourceCallSites)))
                    throw new NotSupportedException("Source code mutation requires its entered loader/message or genuine typed call-site owner.");
                if (_sourceCallSites.TryGetValue(address, out site))
                {
                    RequireSourceCallSiteIdentity(site, plugin);
                    if (site.Failure is not null || site.Phase is NativeSourceCallSitePhase.Retired or NativeSourceCallSitePhase.Closed)
                        throw new InvalidDataException("Source CALL cannot replay a failed or retired site.");
                    Finish(reader); Record(); return site.Id;
                }
                if (_nextSourceCallSite >= 256) throw new NotSupportedException("Native source CALL lifetime quota is exhausted.");
                _sourceCallSiteSource ??= FalloutExecutableStringTable.ReadEngineCallSiteSource(host.RuntimePath, host.RuntimeSha256);
                var declaration = _sourceCallSiteSource.Read(address);
                // One authored return byte follows the five-byte CALL. It must
                // remain inside the exact source image; no source byte is copied.
                if ((ulong)address + 6 > (ulong)host.SourceAddressSpace.ImageBase + host.SourceAddressSpace.ImageBytes)
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
                if (site.Phase != NativeSourceCallSitePhase.Declared || lease == 0 || target == 0)
                    throw new InvalidDataException("Source CALL repeated or omitted its first-party publication.");
                if (ReadSourceCallSiteTarget(site, plugin, target, 0x20) != target)
                    throw new InvalidDataException("Source CALL lacks exact native first-party bytes/target readback.");
                site.Lease = lease; site.DefaultTarget = site.CurrentTarget = target;
                site.Phase = NativeSourceCallSitePhase.Published; Finish(reader); Record(); return 1;
            }
            if (operation == NativeNvseHostCall.SourceCallSiteTransition)
            {
                action = reader.ReadUInt32(); var requested = reader.ReadUInt32();
                sdkResult = reader.ReadUInt32(); last = reader.ReadUInt32(); var observedOld = reader.ReadUInt32();
                Finish(reader);
                if (sdkResult == 0)
                {
                    Record(); throw new Win32Exception(unchecked((int)last.Value), "Source CALL's actual Windows protection/cache operation failed.");
                }
                if (action == 1)
                {
                    if (requested != 0x40 || observedOld != 0x20 || site.Calls != 0 ||
                        site.Phase is not (NativeSourceCallSitePhase.Published or NativeSourceCallSitePhase.Flushed))
                        throw new InvalidDataException("Source CALL writable transition escaped its actual source/publication prefix.");
                    old = observedOld; site.PreviousProtection = observedOld; site.Phase = NativeSourceCallSitePhase.Writable;
                }
                else if (action == 2)
                {
                    if (site.Phase != NativeSourceCallSitePhase.Writable || requested != site.PreviousProtection || observedOld != 0x40)
                        throw new InvalidDataException("Source CALL protection did not restore its actual prior executable access.");
                    old = observedOld; site.CurrentTarget = ReadSourceCallSiteTarget(site, plugin, site.DefaultTarget, requested);
                    site.Mutations = checked(site.Mutations + 1); site.Phase = NativeSourceCallSitePhase.Restored;
                }
                else if (action == 3)
                {
                    if (requested != 0 || site.Phase != NativeSourceCallSitePhase.Restored ||
                        ReadSourceCallSiteTarget(site, plugin, site.DefaultTarget, 0x20) != site.CurrentTarget)
                        throw new InvalidDataException("Source CALL has no exact restored instruction-cache publication prefix.");
                    site.Phase = NativeSourceCallSitePhase.Flushed;
                }
                else throw new InvalidDataException("Source CALL SDK transition has an unowned action.");
                Record(); return 1;
            }
            if (operation == NativeNvseHostCall.SourceCallSiteExecute)
            {
                var nativeReceiver = reader.ReadUInt32(); Finish(reader);
                var invocation = _sourceCallSiteInvocation ?? throw new NotSupportedException("Source CALL has no genuine C# object invocation lease.");
                receiverId = invocation.Receiver.Id;
                if (!ReferenceEquals(invocation.Site, site) || invocation.Receiver.Address != nativeReceiver || site.Calls != 1 ||
                    site.Replacement || site.Phase is not (NativeSourceCallSitePhase.Published or NativeSourceCallSitePhase.Flushed))
                    throw new InvalidDataException("Pure getter callback lost its bound object/source/first-party target prefix.");
                var current = ReadSourceGetterPointer(plugin, site, invocation.Receiver);
                if (current.Pointer != invocation.ExpectedPointer || !ReferenceEquals(current.Owner, invocation.PointerOwner))
                    throw new InvalidDataException("Authoritative pointer changed inside the actual typed getter call.");
                pointerResult = current.Pointer; Record(); return current.Pointer;
            }
            if (operation == NativeNvseHostCall.SourceCallSiteRetire)
            {
                var lease = reader.ReadUInt64(); Finish(reader);
                if (_operation != nameof(NativePluginDomainOperation.UnloadNvse) || lease != site.Lease || site.Calls != 0 ||
                    site.Phase is NativeSourceCallSitePhase.Declared or NativeSourceCallSitePhase.Writable or NativeSourceCallSitePhase.Retired or NativeSourceCallSitePhase.Closed)
                    throw new InvalidDataException("Source CALL retirement escaped its real empty original-module unload.");
                _ = _process.ReadSourceCallSite(address, host.SourceAddressSpace.ImageBase, host.SourceAddressSpace.ImageBytes,
                    site.DefaultTarget, plugin.Image, 0x20, retired: true);
                site.Phase = NativeSourceCallSitePhase.Retired; Record(); return 1;
            }
            throw new InvalidDataException("Unknown source CALL callback operation.");
        }
        catch (Exception failure)
        {
            if (site is not null) site.Failure ??= failure.Message;
            Record(failure.Message); throw;
        }
        void Record(string? failure = null) => _sourceCallSiteEvents.Add(new(Generation, plugin.Module, NativeThread,
            frame.Id, parent, operation, site?.Id ?? 0, address, host.RuntimeSha256,
            site?.Phase ?? NativeSourceCallSitePhase.Declared, site?.CurrentTarget ?? 0,
            action, sdkResult, last, old, receiverId, pointerResult, failure));
    }

    private void RequireSourceCallSiteIdentity(NativeSourceCallSite site, NativeNvsePlugin plugin)
    {
        if (site.Generation != Generation || site.Module != plugin.Module || site.Thread != NativeThread ||
            !_sourceCallSites.TryGetValue(site.Source.Address, out var retained) || !ReferenceEquals(site, retained))
            throw new InvalidDataException("Source CALL belongs to another module/thread/generation declaration.");
    }
    private uint ReadSourceCallSiteTarget(NativeSourceCallSite site, NativeNvsePlugin plugin, uint target, uint protection)
    {
        var host = _nvseHostSource ?? throw new InvalidDataException("Source CALL has no host lease.");
        return _process.ReadSourceCallSite(site.Source.Address, host.SourceAddressSpace.ImageBase,
            host.SourceAddressSpace.ImageBytes, target, plugin.Image, protection);
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
