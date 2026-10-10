namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private bool EngineGlobalSourcesRetired => _failedEngineGlobalSourceLeases.Count == 0 && _engineGlobals.Values.All(value => value.Phase == NativeEngineGlobalPhase.Closed) &&
        (_engineTls is null || _engineTls.CarrierLease is null && _engineTls.CurrentLease is null);
    // Called by a complete actual thread/interpreter/task constructor after
    // all runtime fields exist. Template bytes alone cannot supply this lease.
    internal void BindNvseEngineCurrentTls(NativeNvsePlugin plugin, NativeNvseEngineTlsAuthority authority)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); var tls = RequireEngineTls(plugin);
        ArgumentNullException.ThrowIfNull(authority); RequireEngineAuthorityIdentity(authority.RuntimeSha256, authority.SourceOwner);
        if (tls.CurrentAuthority is not null) throw new InvalidOperationException("Current compiler TLS cannot replace an entered source owner.");
        authority.RequireCurrent(NativeThread); authority.RequireSourceCallbacks(tls.Source);
        var image = ReadCompleteCurrentEngineTls(tls, authority);
        var lease = authority.Retain(); tls.CurrentAuthority = authority; tls.CurrentLease = lease;
        PublishCurrentEngineTls(plugin, tls, image);
    }

    internal void RefreshNvseEngineCurrentTls(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); var tls = RequireEngineTls(plugin);
        var authority = tls.CurrentAuthority ?? throw new NotSupportedException("Current compiler TLS has no actual complete runtime field producer.");
        authority.RequireCurrent(NativeThread); authority.RequireSourceCallbacks(tls.Source);
        PublishCurrentEngineTls(plugin, tls, ReadCompleteCurrentEngineTls(tls, authority));
    }

    private static byte[] ReadCompleteCurrentEngineTls(NativeEngineStaticTls tls, NativeNvseEngineTlsAuthority authority)
    {
        var image = authority.ReadCompleteImage(tls.Source);
        if (image is null || image.Length != tls.Source.InitialBytes)
            throw new InvalidDataException("Current TLS authority omits or substitutes actual declared runtime fields.");
        return image.ToArray();
    }
    private void PublishCurrentEngineTls(NativeNvsePlugin plugin, NativeEngineStaticTls tls, byte[] image)
    {
        if (tls.PendingImage is not null || tls.Retired || tls.Failure is not null)
            throw new InvalidOperationException("Current TLS publication overlaps a failed/entered/retired prefix.");
        ++_callDepth;
        try
        {
            if (!_process.ReadEngineStaticTls(tls.Pointer, tls.Source.InitialBytes).AsSpan().SequenceEqual(tls.PublishedImage))
                throw new NotSupportedException("Original TLS bytes changed without their authoritative current-context mutation owner.");
            // Retain this pending prefix on failure: after entered native copying,
            // the old image may no longer describe what the original module sees.
            tls.PendingImage = image;
            using var reader = Exchange(NativePluginDomainOperation.SourceGlobals, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(2U); writer.Write(tls.Source.InitialBytes);
                writer.Write(tls.PublishedImage); writer.Write(image);
            }));
            if (reader.ReadUInt32() != tls.Image || reader.ReadUInt32() != tls.Index || reader.ReadUInt32() != tls.Pointer ||
                reader.ReadUInt32() != tls.Extent || !tls.SourceConstructed || tls.PendingImage is not null)
                throw new InvalidDataException("Current TLS publication has no correlated actual native/source receipt.");
            Finish(reader);
        }
        catch (Exception failure) { tls.Failure ??= failure.Message; throw; }
        finally { --_callDepth; }
    }

    private void RequireEngineGlobalsCurrent(NativeNvsePlugin plugin)
    {
        var host = EngineGlobalHost();
        foreach (var global in _engineGlobals.Values)
        {
            try
            {
                if (global.Phase != NativeEngineGlobalPhase.Published)
                    throw new InvalidOperationException("Original call has an unpublished/retired engine global.");
                RequireEngineGlobalCurrent(global, plugin);
                if (_process.ReadEngineGlobal(global.Address, host.SourceAddressSpace.ImageBase, host.SourceAddressSpace.ImageBytes) != global.Value)
                    throw new InvalidDataException("Original call observes source global bytes outside the actual field authority.");
            }
            catch (Exception failure) { global.Failure ??= failure.Message; throw; }
        }
        if (_engineTls is { } tls)
        {
            try
            {
                _ = RequireEngineTls(plugin);
                if (tls.CurrentAuthority is { } authority)
                {
                    authority.RequireCurrent(NativeThread);
                    if (!ReadCompleteCurrentEngineTls(tls, authority).AsSpan().SequenceEqual(tls.PublishedImage))
                        throw new NotSupportedException("Actual TLS context changed before its genuine native refresh publication.");
                }
                if (!_process.ReadEngineStaticTls(tls.Pointer, tls.Source.InitialBytes).AsSpan().SequenceEqual(tls.PublishedImage))
                    throw new NotSupportedException("Original call changed TLS fields without their current interpreter/event mutation owner.");
            }
            catch (Exception failure) { tls.Failure ??= failure.Message; throw; }
        }
    }

    internal void RequireNvseEngineGlobalsSaveOwned()
    {
        VerifyOwner();
        if (_engineGlobals.Count != 0 || _engineTls is not null)
            throw new NotSupportedException("Original source global/static-TLS pointers and current context require their complete cold reconstruction/continuation owner.");
    }
}
