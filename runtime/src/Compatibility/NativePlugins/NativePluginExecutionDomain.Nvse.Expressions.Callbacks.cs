namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint ExpressionInitialize = 0x100, ExpressionCreate = 0x101, ExpressionExtract = 0x102,
        ExpressionDestroy = 0x103, ExpressionExpectedReturn = 0x104, ExpressionCommandBegin = 0x105;

    private byte[] DispatchNvseExpressionHost(Frame frame, ulong parent)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Expression callback has no original module.");
        if (thread != NativeThread || module != plugin.Module || handle != plugin.Handle || plugin.Generation != Generation)
            throw new InvalidDataException("Expression callback belongs to a foreign module/thread/generation.");
        if (frame.Operation == ExpressionInitialize)
        {
            var destination = reader.ReadUInt32(); var prefix = reader.ReadUInt32(); var extent = reader.ReadUInt32(); Finish(reader);
            if (_nvseExpressionAbi is null || plugin.Phase != NativeNvsePhase.Loading || destination == 0 || prefix != _nvseExpressionAbi.CallableBytes || extent != _nvseExpressionAbi.DeclaredBytes)
                throw new InvalidDataException("Original expression Init lacks its source-declared writable table extent.");
            ++_nvseExpressionInitializations;
            return Payload(writer => writer.Write(1U));
        }
        var callerId = reader.ReadUInt64();
        if (!_nvseExpressionCallers.TryGetValue(callerId, out var caller))
            throw new InvalidDataException("Expression callback has no authoritative C# caller lifetime.");
        if (frame.Operation == ExpressionCommandBegin)
        {
            var source = reader.ReadUInt32(); var opcode = reader.ReadUInt32(); var parameters = reader.ReadUInt32(); var count = reader.ReadUInt32(); var result = reader.ReadUInt32(); var offset = reader.ReadUInt32();
            if (result == 0 || offset == 0 || caller.NativeParameters is not null || source != caller.Command.SourceAddress || opcode != caller.Command.AssignedOpcode ||
                count != caller.Command.Parameters.Length || count != 0 && parameters == 0)
                throw new InvalidDataException("Native command parameter copy lacks its exact retained registration owner.");
            for (var index = 0; index < count; ++index)
            {
                var actual = ReadNvseText(reader); var type = reader.ReadUInt32(); var optional = reader.ReadUInt32(); var declared = caller.Command.Parameters[index];
                if (actual.IsNull != declared.Name.IsNull || !actual.Bytes.AsSpan().SequenceEqual(declared.Name.Bytes.AsSpan()) ||
                    type != declared.Type || optional != declared.Optional)
                    throw new InvalidDataException("Native command parameter copy differs from the actual source registration.");
            }
            Finish(reader); caller.NativeParameters = parameters; caller.NativeResult = result; caller.NativeOffset = offset; return Payload(writer => writer.Write(1U));
        }
        if (frame.Operation == ExpressionCreate)
        {
            var pointer = reader.ReadUInt32(); var parameters = reader.ReadUInt32(); var data = reader.ReadUInt32();
            var thisObject = reader.ReadUInt32(); var container = reader.ReadUInt32(); var script = reader.ReadUInt32(); var events = reader.ReadUInt32();
            var result = reader.ReadUInt32(); var offset = reader.ReadUInt32(); var rawOffset = reader.ReadUInt32(); Finish(reader);
            if (pointer == 0 || caller.NativeParameters is null || parameters != caller.NativeParameters || data != caller.ScriptData.Address ||
                thisObject != 0 || container != 0 || script != caller.NativeScript || events != caller.NativeEventList || result != caller.NativeResult || offset != caller.NativeOffset || rawOffset != caller.Arguments.StartOffset ||
                caller.Evaluators.Values.Any(value => value.Pointer == pointer))
                throw new InvalidDataException("Native expression Create differs from its actual admitted command frame.");
            var evaluator = checked(++_nextNvseExpressionEvaluator);
            caller.Evaluators.Add(evaluator, (pointer, false)); ++caller.Created; ++_nvseExpressionCreated;
            return Payload(writer => writer.Write(evaluator));
        }
        var id = reader.ReadUInt64(); var nativePointer = reader.ReadUInt32();
        if (!caller.Evaluators.TryGetValue(id, out var lifetime) || lifetime.Pointer != nativePointer)
            throw new InvalidDataException("Native expression evaluator is forged, foreign or retired.");
        switch (frame.Operation)
        {
            case ExpressionExtract:
                {
                    var currentOffset = reader.ReadUInt32(); var parser = reader.ReadUInt32(); Finish(reader);
                    if (parser > 1) throw new InvalidDataException("Native expression extraction has an unknown actual API parser.");
                    if (lifetime.Extracted || currentOffset != caller.Arguments.StartOffset)
                        throw new InvalidDataException("Expression extraction has already consumed this source argument extent.");
                    // Evaluation is deferred until the actual original utility call.
                    // Stateful arguments retain their order and are never rerun by
                    // native GetNthArg/getters or a duplicate extraction attempt.
                    caller.Evaluators[id] = (nativePointer, true);
                    var evaluatorOwner = parser == 0 ? caller.Arguments.EvaluateNative ?? caller.Arguments.Evaluate : caller.Arguments.Evaluate;
                    var values = evaluatorOwner() ?? throw new InvalidDataException("Expression argument owner returned no result.");
                    if (values.Values is null || values.Values.Count > byte.MaxValue || values.EndOffset < currentOffset || values.EndOffset > caller.Arguments.MaximumEndOffset)
                        throw new InvalidDataException("Expression argument owner returned an invalid count/source end extent.");
                    var required = caller.Command.Parameters.TakeWhile(parameter => parameter.Optional == 0).Count();
                    if (caller.Command.Parameters.Skip(required).Any(parameter => parameter.Optional == 0) ||
                        values.Values.Count < required || values.Values.Count > caller.Command.Parameters.Length)
                        throw new InvalidDataException("Original extraction count differs from its required/optional registered parameters.");
                    foreach (var value in values.Values) RetainNvseArgumentArrays(callerId, value);
                    return SerializeNvseArguments(values, caller, id);
                }
            case ExpressionLocalRead:
                return ReadExpressionLocal(frame, parent, reader, caller, id, nativePointer);
            case ExpressionDestroy:
                Finish(reader); caller.TokenValues.Remove(id); caller.Evaluators.Remove(id); ++caller.Destroyed; ++_nvseExpressionDestroyed;
                return Payload(writer => writer.Write(1U));
            case ExpressionExpectedReturn:
                {
                    var type = reader.ReadUInt32(); Finish(reader);
                    if (type > 5) throw new InvalidDataException("Expression expected-return declaration is invalid.");
                    if (type is 1 or 4 or 5 || type != 0 && (caller.ResultTarget is null || (uint)caller.ResultTarget.Kind != type))
                        throw new NotSupportedException("Expression expected result lacks its actual typed target/object owner.");
                    caller.ExpectedReturn = (byte)type; return Payload(writer => writer.Write(1U));
                }
            default: throw new InvalidDataException($"Unknown expression callback {frame.Operation} in caller {callerId}, parent {parent}.");
        }
    }

    private byte[] SerializeNvseArguments(NativeNvseEvaluatedArguments values, NativeNvseExpressionCaller caller, ulong evaluator)
    {
        var nodes = new List<(ulong Id, NativeNvseExpressionValue Value, ulong Left, ulong Right)>();
        ulong Add(NativeNvseExpressionValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            ulong left = 0, right = 0;
            if (value.Local is { } local) RequireExpressionLocal(caller, local);
            if (value.Type == NativeNvseTokenType.Pair) { left = Add(value.Left!); right = Add(value.Right!); }
            var id = checked((ulong)nodes.Count + 1); nodes.Add((id, value, left, right)); return id;
        }
        var roots = values.Values.Select(Add).ToArray();
        caller.TokenValues.Add(evaluator, nodes.Select(node => node.Value).ToArray());
        var payload = Payload(writer =>
        {
            writer.Write(checked((uint)roots.Length)); writer.Write(values.EndOffset); writer.Write(checked((uint)nodes.Count));
            foreach (var node in nodes)
            {
                writer.Write(node.Id); writer.Write((uint)node.Value.Type);
                switch (node.Value.Type)
                {
                    case NativeNvseTokenType.Number: case NativeNvseTokenType.Boolean: writer.Write(node.Value.Number); break;
                    case NativeNvseTokenType.Form: case NativeNvseTokenType.Array: writer.Write(checked((uint)node.Value.Number)); break;
                    case NativeNvseTokenType.String: writer.Write(checked((uint)node.Value.Text.Length)); writer.Write(node.Value.Text.AsSpan()); break;
                    case NativeNvseTokenType.NumericVariable:
                    case NativeNvseTokenType.ReferenceVariable:
                    case NativeNvseTokenType.StringVariable:
                    case NativeNvseTokenType.ArrayVariable:
                        var local = node.Value.Local ?? throw new InvalidDataException("Native variable token has no actual local.");
                        writer.Write(local.Context.Id); writer.Write(checked((uint)local.Entry)); writer.Write(local.Index); writer.Write(local.CachedStringIdentity); break;
                    case NativeNvseTokenType.Pair: writer.Write(node.Left); writer.Write(node.Right); break;
                    default: throw new NotSupportedException("Expression token category has no native owner.");
                }
            }
            foreach (var root in roots) writer.Write(root);
        });
        caller.Tokens = checked(caller.Tokens + (uint)nodes.Count); return payload;
    }
}
