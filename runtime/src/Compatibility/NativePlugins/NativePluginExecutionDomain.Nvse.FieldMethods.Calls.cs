namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    internal NativeSourceFieldMethod PrepareNvseSourceFieldMethod(NativeNvsePlugin plugin, uint sourceEntry,
        NativePluginAbi abi, NativeNvseSourceClass? receiverClass)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall();
        if (abi is not (NativePluginAbi.Cdecl or NativePluginAbi.Thiscall) ||
            (abi == NativePluginAbi.Cdecl ? receiverClass is not null : receiverClass is null))
            throw new NotSupportedException("Source field method requires an explicit owned zero-stack-argument cdecl/thiscall contract.");
        ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.SourceFieldMethods, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(0U); writer.Write(sourceEntry);
                writer.Write((uint)abi); writer.Write(receiverClass is { } value ? (uint)value : 0U);
            }));
            var id = reader.ReadUInt32(); var entry = reader.ReadUInt32(); Finish(reader);
            if (!_sourceFieldMethods.TryGetValue(entry, out var method) || entry != sourceEntry || method.Id != id ||
                method.Phase != NativeSourceFieldMethodPhase.Published || method.Failure is not null ||
                method.Abi != abi || method.ReceiverClass != receiverClass)
                throw new InvalidDataException("Source field method has no exact native publication/ABI receipt.");
            RequireSourceFieldMethod(method, plugin); return method;
        }
        finally { --_callDepth; }
    }

    internal NativeSourceFieldMethodReceipt CallNvseSourceFieldMethod(NativeNvsePlugin plugin,
        NativeSourceFieldMethod method, NativeNvseSourceObject? receiver)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); RequireSourceFieldMethod(method, plugin);
        if (method.Phase != NativeSourceFieldMethodPhase.Published || method.Failure is not null || method.Calls != 0 ||
            _sourceFieldMethodInvocation is not null)
            throw new InvalidOperationException("Source field method cannot overlap or replay an unpublished/failed invocation.");
        var expected = EvaluateSourceFieldMethod(plugin, method, receiver);
        _process.RequireEngineCommandCallable(method.Source.Entry, method.Target, retired: false);
        var invocation = new NativeSourceFieldMethodInvocation(method, receiver, expected);
        _sourceFieldMethodInvocation = invocation; ++method.Calls; if (receiver is not null) ++receiver.Calls;
        ++_callDepth; var first = _sourceFieldMethodEvents.Count;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.SourceFieldMethods, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(1U); writer.Write(method.Id); writer.Write(method.Source.Entry);
                writer.Write(receiver?.Id ?? 0UL); writer.Write(receiver?.Address ?? 0U);
            }));
            var call = reader.ReadUInt64(); var value = reader.ReadUInt32(); var stack = reader.ReadInt32();
            var preserved = reader.ReadUInt32(); var callbacks = reader.ReadUInt32(); Finish(reader);
            var events = _sourceFieldMethodEvents.Skip(first).Where(value => value.Operation == NativeNvseHostCall.SourceFieldMethodExecute).ToArray();
            if (events.Length != 1 || events[0].Failure is not null || events[0].Method != method.Id || events[0].Parent != call ||
                events[0].Receiver != receiver?.Id || events[0].Result != expected.Bits || events[0].Role != expected.Role ||
                value != expected.Bits || stack != 0 || preserved != 7 || callbacks != 1 || !invocation.Returned || invocation.Callback != events[0].Callback)
                throw new InvalidDataException("Source field call lacks its exact callback/object/result/native ABI receipt.");
            if (receiver is not null) VerifyNvseSourceObject(plugin, receiver);
            if (expected.Object is { } target) VerifyNvseSourceObject(plugin, target);
            if (expected.Buffer is { } buffer) VerifyGuest(buffer);
            return new(Generation, plugin.Module, method.Id, call, invocation.Callback, receiver?.Id, value, expected.Role,
                expected.Buffer?.Handle ?? expected.Object?.Image.Handle, stack, preserved);
        }
        catch (Exception failure) { method.Failure ??= failure.Message; throw; }
        finally
        {
            --_callDepth; --method.Calls; if (receiver is not null) --receiver.Calls;
            _sourceFieldMethodInvocation = null;
        }
    }

    private void RequireSourceFieldAllocationIdle(NativePluginGuestAllocation allocation)
    {
        if (_sourceFieldMethods.Values.Any(method => method.Borrows.Any(borrow => borrow.Allocation.Handle == allocation.Handle)))
            throw new InvalidOperationException("An original source method still retains this actual returned pointer allocation.");
    }
}
