using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    internal NativeSourceCallSite PrepareNvseSourceCallSite(NativeNvsePlugin plugin, uint sourceAddress)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall();
        ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.SourceCallSites, Payload(writer =>
            { writer.Write(plugin.Module); writer.Write(0U); writer.Write(sourceAddress); }));
            var id = reader.ReadUInt32(); var address = reader.ReadUInt32(); Finish(reader);
            if (address != sourceAddress || !_sourceCallSites.TryGetValue(address, out var site) || site.Id != id ||
                site.Phase != NativeSourceCallSitePhase.Published || site.Replacement || site.Failure is not null)
                throw new InvalidDataException("Source CALL preparation lacks its genuine first-party publication receipt.");
            RequireSourceCallSiteIdentity(site, plugin); return site;
        }
        finally { --_callDepth; }
    }

    // This is a product callable boundary over the existing complete native
    // class graph. It does not manufacture a SceneGraph, camera, TES or player
    // object. Installed replacement hooks require their own genuine semantic
    // authority and remain refused here even when their target is executable.
    internal NativeSourceCallSiteReceipt CallNvseSourceCallSite(NativeNvsePlugin plugin,
        NativeSourceCallSite site, NativeNvseSourceObject? receiver)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); RequireSourceCallSiteIdentity(site, plugin);
        if (_sourceCallSiteInvocation is not null || site.Calls != 0 || site.Failure is not null ||
            site.Phase is not (NativeSourceCallSitePhase.Published or NativeSourceCallSitePhase.Flushed))
            throw new InvalidOperationException("Source CALL overlaps an entered/failed/unpublished object invocation.");
        if (site.Suppressed) return ObserveSourceSuppression(plugin, site);
        if (site.Source.Getter is null)
            throw new NotSupportedException("Unsuppressed source CALL retains its unowned actual runtime caller/target semantics.");
        if (receiver is null) throw new NotSupportedException("Pointer getter requires its genuine source receiver.");
        VerifyNvseSourceObject(plugin, receiver);
        if (site.Replacement)
            throw new NotSupportedException("Installed original hook requires its complete typed runtime object/global/effect consumer; executable target identity is insufficient.");
        if (ReadSourceCallSiteCode(site, plugin, site.DefaultTarget, site.Protection) != site.Code)
            throw new InvalidDataException("Source CALL changed before its genuine getter invocation.");
        var expected = ReadSourceGetterPointer(plugin, site, receiver);
        _sourceCallSiteInvocation = new(site, receiver, expected.Pointer, expected.Owner);
        ++site.Calls; ++receiver.Calls; if (expected.Owner is { } target) ++target.Calls;
        ++_callDepth; var firstEvent = _sourceCallSiteEvents.Count;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.SourceCallSites, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(1U); writer.Write(site.Id); writer.Write(site.Source.Address);
                writer.Write(receiver.Id); writer.Write(receiver.Address);
            }));
            var call = reader.ReadUInt64(); var result = reader.ReadUInt32(); var stack = reader.ReadInt32();
            var preserved = reader.ReadUInt32(); var getterCalls = reader.ReadUInt32(); Finish(reader);
            var rows = _sourceCallSiteEvents.Skip(firstEvent).Where(row => row.Event == NativeNvseHostCall.SourceCallSiteExecute).ToArray();
            if (rows.Length != 1 || rows[0].Failure is not null || rows[0].Parent != call || rows[0].Site != site.Id ||
                rows[0].Receiver != receiver.Id || rows[0].PointerResult != expected.Pointer ||
                result != expected.Pointer || stack != 0 || preserved != 7 || getterCalls != 1)
                throw new InvalidDataException("Source getter lacks its exact native callback/result/ABI/object receipt.");
            VerifyNvseSourceObject(plugin, receiver); if (expected.Owner is { } current) VerifyNvseSourceObject(plugin, current);
            return new(Generation, plugin.Module, site.Id, call, receiver.Id, result, stack, preserved, getterCalls);
        }
        catch (Exception failure) { site.Failure ??= failure.Message; throw; }
        finally
        {
            --_callDepth; --site.Calls; --receiver.Calls;
            if (expected.Owner is { } retained) --retained.Calls;
            _sourceCallSiteInvocation = null;
        }
    }

    private (uint Pointer, NativeNvseSourceObject? Owner) ReadSourceGetterPointer(NativeNvsePlugin plugin,
        NativeSourceCallSite site, NativeNvseSourceObject receiver)
    {
        VerifyNvseSourceObject(plugin, receiver);
        if (receiver.Authority is not INativeNvseSourcePointerGetterAuthority method)
            throw new NotSupportedException("Source pointer getter requires its actual complete source-class method/member/nullability owner.");
        method.RequireSourcePointerGetter(site.Source, receiver);
        receiver.Authority.RequireCurrent();
        if (!_nvseSourceGraphs.TryGetValue(receiver.Id, out var graph) || graph.Retired ||
            !graph.NativeImages.TryGetValue(receiver.Id, out var image))
            throw new NotSupportedException("Pointer getter receiver has no complete current native field/topology graph.");
        var offset = (site.Source.Getter ?? throw new NotSupportedException("Source target has no complete pointer-getter declaration.")).MemberOffset;
        if (offset < 0 || offset > image.Length - 4 || receiver.Image.Length != image.Length)
            throw new InvalidDataException("Source getter member lies outside its actual complete receiver class.");
        var pointer = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset));
        if (_process.ReadSourceCallSiteMember(receiver.Address, checked((uint)offset)) != pointer)
            throw new InvalidDataException("Native member bytes changed outside the authoritative class field owner.");
        if (pointer == 0) return (0, null); // actual published null field, never an absent owner default
        var owner = receiver.Dependencies.SingleOrDefault(value => value.Address == pointer);
        if (owner is null) throw new NotSupportedException("Source getter pointer has no actual retained native-object dependency lease.");
        VerifyNvseSourceObject(plugin, owner); return (pointer, owner);
    }

    internal NativeSourceCallSite RetainedNvseSourceCallSite(NativeNvsePlugin plugin, uint sourceAddress)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall();
        if (!_sourceCallSites.TryGetValue(sourceAddress, out var site)) throw new InvalidDataException("Code site has no actual original-module publication.");
        RequireSourceCallSiteIdentity(site, plugin);
        if (site.Failure is not null || site.Phase != NativeSourceCallSitePhase.Flushed ||
            !site.CachePublished || ReadSourceCallSiteCode(site, plugin, site.DefaultTarget, site.Protection) != site.Code)
            throw new InvalidOperationException("Code site retains an incomplete/failed or changed mutation prefix.");
        return site;
    }

    // A cold record contains semantic/source identity only. The fresh original
    // module must install and flush the patch itself; no pointer, page lease or
    // old callback is restored or replayed from a save.
    internal NativeSourceSuppressionCold CaptureNvseSuppression(NativeNvsePlugin plugin, NativeSourceCallSite site)
    {
        RequireSourceCallSiteIdentity(site, plugin);
        _ = RetainedNvseSourceCallSite(plugin, site.Source.Address);
        if (!site.Suppressed) throw new NotSupportedException("Code-site cold suppression requires its actual NOP publication.");
        return new(site.Source.RuntimeSha256, site.Source.Identity, site.Source.DeclarationSha256, site.Code!.Sha256, site.Protection);
    }
    internal void RequireNvseSuppressionCold(NativeNvsePlugin plugin, NativeSourceCallSite site, NativeSourceSuppressionCold cold)
    {
        if (CaptureNvseSuppression(plugin, site) != cold) throw new InvalidDataException("Fresh source suppression differs from its neutral cold identity.");
    }

    private NativeSourceCallSiteReceipt ObserveSourceSuppression(NativeNvsePlugin plugin, NativeSourceCallSite site)
    {
        if (site.Phase != NativeSourceCallSitePhase.Flushed ||
            !site.CachePublished || ReadSourceCallSiteCode(site, plugin, site.DefaultTarget, site.Protection) != site.Code)
            throw new InvalidDataException("Source suppression has no exact flushed native mutation receipt.");
        ++site.Calls; ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.SourceCallSites, Payload(writer =>
            { writer.Write(plugin.Module); writer.Write(2U); writer.Write(site.Id); writer.Write(site.Source.Address); }));
            var call = reader.ReadUInt64(); var kind = reader.ReadUInt32(); var executed = reader.ReadUInt32(); Finish(reader);
            if (kind != (uint)NativeSourceCallSiteCodeKind.Suppressed || executed != 0 ||
                ReadSourceCallSiteCode(site, plugin, site.DefaultTarget, site.Protection) != site.Code)
                throw new InvalidDataException("Suppression observation executed a target or lost its actual code identity.");
            // No target call occurred: there is no pointer result, target stack
            // delta, preserved-register assertion or fabricated source return.
            return new(Generation, plugin.Module, site.Id, call, null, null, null, null, 0, true, site.Code!.Sha256);
        }
        catch (Exception failure) { site.Failure ??= failure.Message; throw; }
        finally { --_callDepth; --site.Calls; }
    }
}
