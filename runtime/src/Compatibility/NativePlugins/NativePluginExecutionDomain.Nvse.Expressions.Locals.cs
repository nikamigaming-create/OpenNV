namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint ExpressionLocalRead = 0x106;
    private readonly List<NativeNvseExpressionLocalReceipt> _nvseExpressionLocalReceipts = [];
    internal IReadOnlyList<NativeNvseExpressionLocalReceipt> NvseExpressionLocalReceipts => _nvseExpressionLocalReceipts.AsReadOnly();

    private void RequireExpressionLocal(NativeNvseExpressionCaller caller, NativeNvseExpressionLocal local)
    {
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Expression local has no living original module.");
        VerifyNvseLocalContext(plugin, local.Context); local.RequireCurrent();
        if (!ReferenceEquals(caller.LocalContext, local.Context) || local.Context.Active <= 0 ||
            _nvseLocalCallers.Count == 0 || _nvseLocalCallers[^1] != caller.Id || local.Context.Transfer is not null ||
            caller.SourceScript is not null && !ReferenceEquals(local.Context.Script, caller.SourceScript))
            throw new InvalidDataException("Expression local is outside its exact active caller/source event-list.");
        if (local.Type is NativeNvseTokenType.StringVariable or NativeNvseTokenType.ArrayVariable)
        {
            var values = _nvseValues ?? throw new NotSupportedException("Expression local has no shared string/array store.");
            if (!ReferenceEquals(values.StoreIdentity, local.Context.Authority.ValueStoreIdentity))
                throw new InvalidDataException("Expression local and value store do not share the actual campaign authority.");
            if (local.Type == NativeNvseTokenType.StringVariable && local.CachedStringIdentity != 0 &&
                values.GetString(local.CachedStringIdentity) is null)
                throw new NotSupportedException("Expression token's resolved string object lifetime has retired.");
            if (local.Type == NativeNvseTokenType.ArrayVariable) RetainNvseArray(caller.Id, NativeNvseExpressionLocal.Handle(local.Read()));
        }
    }

    private byte[] ReadExpressionLocal(Frame frame, ulong parent, BinaryReader reader,
        NativeNvseExpressionCaller caller, ulong evaluator, uint nativeEvaluator)
    {
        var token = reader.ReadUInt64(); var type = (NativeNvseTokenType)reader.ReadUInt32();
        var context = reader.ReadUInt64(); var entry = reader.ReadUInt32(); var index = reader.ReadUInt32();
        var address = reader.ReadUInt32(); var observed = reader.ReadUInt64(); Finish(reader);
        if (!caller.TokenValues.TryGetValue(evaluator, out var nodes) || token == 0 || token > (ulong)nodes.Length ||
            !caller.Evaluators.TryGetValue(evaluator, out var lifetime) || !lifetime.Extracted || lifetime.Pointer != nativeEvaluator)
            throw new InvalidDataException("Expression local callback has no extracted evaluator/token lifetime.");
        var value = nodes[checked((int)token - 1)];
        var local = value.Local ?? throw new NotSupportedException("Expression token has no source-produced local cell.");
        RequireExpressionLocal(caller, local);
        var actual = local.Read();
        if (type != value.Type || context != local.Context.Id || entry != local.Entry || index != local.Index ||
            address != local.Address || observed != actual)
            throw new InvalidDataException("Expression local callback differs from its current original cell identity/payload.");
        _nvseExpressionLocalReceipts.Add(new(Generation, frame.Id, parent, caller.Id, evaluator, token,
            context, local.Entry, index, type, address, actual));
        return Payload(writer => { writer.Write(address); writer.Write(actual); });
    }
}
