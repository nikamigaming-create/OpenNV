using System.Buffers.Binary;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptHitHandlerState(FalloutFormKey Script, FalloutFormKey? Filter,
    long Invocations, long Executions, FalloutFormKey? LastCaller, string? Error);
internal sealed record FalloutScriptHitHandlerResult(FalloutFormKey Script, FalloutFormKey Caller,
    FalloutFormKey? Filter, string? Error);

// Registrations contain source identities, not functions belonging to an old
// world. The contact owner supplies its current executor at dispatch.
internal sealed class FalloutScriptHitHandlers
{
    private sealed class Callback(FalloutFormKey script, FalloutFormKey? filter)
    {
        internal readonly FalloutFormKey Script = script;
        internal readonly FalloutFormKey? Filter = filter;
        internal long Invocations, Executions;
        internal FalloutFormKey? LastCaller;
        internal string? Error;
    }

    private readonly Dictionary<(FalloutFormKey Script, FalloutFormKey? Filter), Callback> _callbacks = [];
    private readonly List<Callback> _order = [];
    private int _depth;
    internal int Count => _callbacks.Count;
    internal IReadOnlyList<FalloutScriptHitHandlerState> State => _order.Select(callback =>
        new FalloutScriptHitHandlerState(callback.Script, callback.Filter, callback.Invocations,
            callback.Executions, callback.LastCaller, callback.Error)).ToArray();

    internal static FalloutScriptFunction RegistrationFunction(FalloutPluginStack records,
        Func<FalloutScriptHitHandlers> owner) =>
        FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Value,
            FalloutScriptArgumentKind.OptionalValue], arguments =>
        {
            owner().Set(records, arguments.Select(argument => argument.Value).ToArray());
            return 0;
        });

    internal void Set(FalloutPluginStack records, IReadOnlyList<FalloutScriptValue> arguments)
    {
        if (arguments.Count is < 2 or > 3)
            throw new InvalidDataException("SetOnHitEventHandler takes a script, registration flag and optional actor or FLST.");
        var script = arguments[0].FormKey(records);
        var definition = records.GetEffective(script);
        if (definition.Signature != "SCPT") throw new InvalidDataException("Hit handler is not a typed SCPT form.");
        var flag = arguments[1];
        if (flag.Kind != FalloutScriptValueKind.Number || flag.Number != Math.Truncate(flag.Number) ||
            flag.Number is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException("Hit handler registration flag must be a signed integer.");
        var register = flag.Number != 0;
        var filter = arguments.Count == 3 ? arguments[2] : FalloutScriptValue.Form(0);
        if (filter.Kind is not (FalloutScriptValueKind.Form or FalloutScriptValueKind.Number) ||
            filter.Kind == FalloutScriptValueKind.Number && filter.Number != 0)
            throw new InvalidDataException("Hit handler filter is not an actor, FLST or null form.");
        var targets = Targets(records, filter);
        // Removal needs only the compiled identity, including when the source
        // body is unsupported. Registration validates before changing any scope.
        if (register) _ = FalloutUserFunction.Read(definition);
        foreach (var target in targets)
        {
            var key = (script, target);
            if (!register)
            {
                if (_callbacks.Remove(key, out var removed)) _order.Remove(removed);
            }
            else if (!_callbacks.ContainsKey(key))
            {
                var callback = new Callback(script, target);
                _callbacks.Add(key, callback);
                _order.Add(callback);
            }
        }
    }

    private static IReadOnlyList<FalloutFormKey?> Targets(FalloutPluginStack records, FalloutScriptValue filter)
    {
        if (filter.Number == 0) return [null];
        var form = filter.FormKey(records);
        if (FalloutReferenceHitEvents.IsActor(records, form)) return [form];
        var list = records.GetEffective(form);
        if (list.Signature != "FLST") throw new InvalidDataException("Hit filter must be an actor reference or FLST.");
        var targets = new HashSet<FalloutFormKey>();
        foreach (var field in list.ReadSubrecords().Where(field => field.Signature == "LNAM"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Hit filter FLST has a malformed LNAM.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
            if (id == 0) continue;
            var member = list.Plugin.AdjustFormId(id);
            // The published filter expands one level and ignores non-actors;
            // actor bases and nested lists are not actor reference filters.
            if (FalloutReferenceHitEvents.IsActor(records, member)) targets.Add(member);
        }
        return targets.Select(target => (FalloutFormKey?)target).ToArray();
    }

    internal IReadOnlyList<FalloutScriptHitHandlerResult> Dispatch(FalloutPluginStack records, FalloutFormKey actor,
        Action<FalloutFormKey, FalloutFormKey> invoke)
    {
        FalloutReferenceHitEvents.RequireActor(records, actor);
        if (_depth >= 30) throw new NotSupportedException("Hit callback recursion exceeds 30 dispatches.");
        if (_callbacks.Count == 0) return [];
        // Both scopes are admitted before the first callback. A registration
        // during an unfiltered callback cannot join this hit's filtered suffix.
        var admitted = _order.Where(callback => callback.Filter is null)
            .Concat(_order.Where(callback => callback.Filter == actor)).ToArray();
        var results = new List<FalloutScriptHitHandlerResult>();
        ++_depth;
        try
        {
            foreach (var callback in admitted)
            {
                if (callback.Error is not null ||
                    !_callbacks.TryGetValue((callback.Script, callback.Filter), out var current) ||
                    !ReferenceEquals(callback, current)) continue;
                ++callback.Invocations;
                callback.LastCaller = actor;
                try
                {
                    invoke(callback.Script, actor);
                    ++callback.Executions;
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or
                    InvalidOperationException or KeyNotFoundException or OverflowException)
                {
                    callback.Error = error.Message;
                }
                results.Add(new(callback.Script, actor, callback.Filter, callback.Error));
            }
        }
        finally { --_depth; }
        return results.AsReadOnly();
    }
}
