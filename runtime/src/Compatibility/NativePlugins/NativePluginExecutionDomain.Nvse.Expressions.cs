namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private NativeNvseExpressionAbi? _nvseExpressionAbi;
    private readonly Dictionary<ulong, NativeNvseExpressionCaller> _nvseExpressionCallers = [];
    private ulong _nextNvseExpressionCaller, _nextNvseExpressionEvaluator;
    private uint _nvseExpressionInitializations, _nvseExpressionCreated, _nvseExpressionDestroyed;

    internal void ConfigureNvseExpressionAbi(NativeNvsePlugin plugin, NativeNvseExpressionAbi declaration)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(declaration);
        if (plugin.Phase != NativeNvsePhase.QueriedTrue || _nvseExpressionAbi is not null ||
            !string.Equals(declaration.PluginSha256, plugin.Sha256, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(declaration.DeclarationOwner) || declaration.CallableBytes is not (88 or 92) || declaration.DeclaredBytes < declaration.CallableBytes ||
            declaration.DeclaredBytes > 4096 || declaration.DeclaredBytes % 4 != 0)
            throw new InvalidDataException("Expression utilities lack an exact selected-client declaration before Load.");
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseExpressionAbi, Payload(writer =>
            { writer.Write(plugin.Module); writer.Write(declaration.DeclaredBytes); writer.Write(declaration.CallableBytes); writer.Write(declaration.ArrayAccessorReturnsNativePointer ? 1U : 0U); }));
            if (reader.ReadUInt32() != declaration.CallableBytes || reader.ReadUInt32() != declaration.DeclaredBytes)
                throw new InvalidDataException("Native expression callable prefix/declared extent drifted.");
            Finish(reader); _nvseExpressionAbi = declaration;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    // A parent/Script projection remains absent. The local-only overload
    // supplies the actual campaign event-list cells, never a TESObjectREFR.
    // Both paths retain original command/data/parameter/result ownership.
    internal NativeNvseExpressionCallerReceipt CallNvseObjectlessCommand(NativeNvsePlugin plugin,
        NativeNvseCommand command, NativePluginGuestAllocation scriptData, NativeNvseExpressionArgumentOwner arguments, NativeNvseValueResultTarget? resultTarget = null)
    {
        return CallNvseCommand(plugin, command, scriptData, arguments, resultTarget, null);
    }

    internal NativeNvseExpressionCallerReceipt CallNvseLocalCommand(NativeNvsePlugin plugin, NativeNvseCommand command,
        NativePluginGuestAllocation scriptData, NativeNvseExpressionArgumentOwner arguments, NativeNvseLocalContext context, NativeNvseValueResultTarget? resultTarget = null)
    {
        VerifyNvseLocalContext(plugin, context); RequireNvseLocalCode(context, scriptData);
        return CallNvseCommand(plugin, command, scriptData, arguments, resultTarget, context);
    }

    private NativeNvseExpressionCallerReceipt CallNvseCommand(NativeNvsePlugin plugin, NativeNvseCommand command,
        NativePluginGuestAllocation scriptData, NativeNvseExpressionArgumentOwner arguments, NativeNvseValueResultTarget? resultTarget, NativeNvseLocalContext? localContext, NativeNvseSourceObject? sourceScript = null, IReadOnlyList<NativeNvseSourceObject>? sourceObjects = null)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); VerifyGuest(scriptData); ArgumentNullException.ThrowIfNull(arguments);
        if (_nvseExpressionAbi is null || !plugin.Registry.Commands.Any(row => ReferenceEquals(row, command)) ||
            command.Generation != Generation || command.Module != plugin.Module || command.Execute == 0 || command.NeedsParent != 0 ||
            command.Parse != 0x08000000 && !(command.Parse == 0 && sourceScript is not null && _nvseScriptInterface) && !HasEngineCommandCaller(command) || command.ReturnType is not (NativeNvseCommandReturn.Default or NativeNvseCommandReturn.String or NativeNvseCommandReturn.Array or NativeNvseCommandReturn.Ambiguous) ||
            (command.ReturnType is NativeNvseCommandReturn.String or NativeNvseCommandReturn.Array) && (resultTarget is null || resultTarget.Kind != command.ReturnType) ||
            command.ReturnType == NativeNvseCommandReturn.Default && resultTarget?.Kind == NativeNvseCommandReturn.Array ||
            resultTarget is not null && (_nvseValues is null || string.IsNullOrWhiteSpace(resultTarget.SourceOwner) || resultTarget.Kind is not (NativeNvseCommandReturn.String or NativeNvseCommandReturn.Array)) ||
            scriptData.Access != NativePluginGuestAccess.ReadOnly || arguments.StartOffset > arguments.MaximumEndOffset ||
            arguments.MaximumEndOffset > scriptData.Length || string.IsNullOrWhiteSpace(arguments.SourceOwner) || arguments.Evaluate is null)
            throw new NotSupportedException("Original command lacks its exact objectless expression/numeric caller admission.");
        if (_callDepth >= _maximumDepth) throw Fatal(new InvalidOperationException("Native expression caller depth exceeded its owner budget."));
        var caller = new NativeNvseExpressionCaller(checked(++_nextNvseExpressionCaller), command, scriptData, arguments) { ResultTarget = resultTarget, LocalContext = localContext, SourceScript = sourceScript, SourceObjects = sourceObjects ?? [] };
        var localBegun = false; var valueBegun = false;
        _nvseExpressionCallers.Add(caller.Id, caller); ++_callDepth;
        try
        {
            BeginNvseLocalCall(caller); localBegun = true;
            BeginNvseValueCall(caller); valueBegun = true;
            var engineEvents = _nvseEngineCommandEvents.Count; var engineParent = checked(_nextRequest + 1);
            using var reader = Exchange(NativePluginDomainOperation.NvseCommand, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(command.SourceAddress); writer.Write(command.AssignedOpcode);
                writer.Write(caller.Id); writer.Write(scriptData.Handle); writer.Write(scriptData.Address); writer.Write(scriptData.Length);
                writer.Write(arguments.StartOffset); writer.Write(arguments.MaximumEndOffset); writer.Write((uint)(resultTarget?.Kind ?? NativeNvseCommandReturn.Default));
                writer.Write(localContext?.Id ?? 0); writer.Write(sourceScript?.Id ?? 0);
            }));
            var raw = reader.ReadUInt32(); var stack = reader.ReadInt32(); var registers = reader.ReadUInt32(); var exception = reader.ReadUInt32();
            var number = reader.ReadDouble(); var end = reader.ReadUInt32(); var created = reader.ReadUInt32(); var destroyed = reader.ReadUInt32(); var tokens = reader.ReadUInt32(); Finish(reader);
            CheckNvseBoundary(stack, registers, exception);
            RequireEngineCommandInvocation(caller, engineEvents, engineParent, raw, number, end);
            if ((raw & 255) > 1 || !double.IsFinite(number) || end < arguments.StartOffset || end > arguments.MaximumEndOffset ||
                created != caller.Created || destroyed != caller.Destroyed || tokens != caller.Tokens || created != destroyed || caller.Evaluators.Count != 0 || caller.NativeParameters is null)
                throw new InvalidDataException("Original expression caller has no complete result/offset/token retirement receipt.");
            if ((raw & 255) == 1 && resultTarget is not null && caller.PublishedValue is null)
                throw new InvalidDataException("Original typed command returned success without publishing its genuine C# result target.");
            if (caller.PublishedValue is { } value)
            {
                var expected = value.Type switch
                {
                    NativeNvseElementType.Number => value.Number,
                    NativeNvseElementType.String => (double)(caller.PublishedIdentity ?? throw new InvalidDataException("String result has no published identity.")),
                    NativeNvseElementType.Array => unchecked((int)(caller.PublishedIdentity ?? throw new InvalidDataException("Array result has no published identity."))),
                    _ => throw new NotSupportedException("Native result category has no C# publication owner."),
                };
                if (number != expected) throw new InvalidDataException("Native result bytes drifted from their actual C# assignment publication.");
            }
            VerifyOwner();
            RequireNvseSourceFileCallerComplete(caller.Id); RequireNvseBinaryCallerComplete(caller.Id);
            return new(caller.Id, command.AssignedOpcode, (raw & 255) == 1, raw, number, end, created, destroyed, tokens, stack, registers, exception)
            { TypedResult = caller.PublishedValue, PublishedIdentity = caller.PublishedIdentity };
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
        finally { _nvseExpressionCallers.Remove(caller.Id); --_callDepth; try { if (valueBegun) EndNvseValueCall(caller); } finally { if (localBegun) EndNvseLocalCall(caller); } }
    }

    internal NativeNvseExpressionStatistics NvseExpressionStatistics(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin);
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseExpressionStatistics, Payload(writer => writer.Write(plugin.Module)));
            var row = new NativeNvseExpressionStatistics(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()); Finish(reader);
            if (row.Initializations != _nvseExpressionInitializations || row.Created != _nvseExpressionCreated || row.Destroyed != _nvseExpressionDestroyed ||
                row.Active != _nvseExpressionCallers.Values.Sum(caller => caller.Evaluators.Count) || row.Created - row.Destroyed != row.Active)
                throw new InvalidDataException("Native expression lifecycle ledger differs from its C# owner.");
            return row;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }
    private void ClearNvseExpressionCapabilities()
    { _nvseExpressionCallers.Clear(); _nvseExpressionAbi = null; }
}
