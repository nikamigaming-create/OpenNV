namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutScriptArrayStore
{
    internal FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
    {
        "ar_null" => FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Array(0), readOnly: true),
        "ar_construct" => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String], args => Construct(args[0].Text)),
        "ar_list" => FalloutScriptFunction.Typed([], args =>
        {
            var array = Construct("array");
            foreach (var argument in args) Append(array, argument.Value);
            return array;
        }, variadic: FalloutScriptArgumentKind.Value),
        "ar_size" => new([FalloutScriptArgumentKind.Value], args => Size(args[0].Value)) { ReadOnly = true },
        "ar_haskey" => new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Value],
            args => HasKey(args[0].Value, args[1].Value) ? 1 : 0)
        { ReadOnly = true },
        "ar_append" => new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Value], args =>
        {
            Append(args[0].Value, args[1].Value);
            return 1;
        }),
        "ar_erase" => new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.OptionalValue],
            args => Erase(args[0].Value, args.Count == 2 ? args[1].Value : (FalloutScriptValue?)null)),
        "ar_resize" => new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Number, FalloutScriptArgumentKind.OptionalValue], args =>
        {
            Resize(args[0].Value, args[1].Number, args.Count == 3 ? args[2].Value : 0);
            return 1;
        }),
        "ar_copy" => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value], args => Copy(args[0].Value)),
        "ar_deepcopy" => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value], args => Copy(args[0].Value, deep: true)),
        "typeof" => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value], args => args[0].Value.Kind switch
        {
            FalloutScriptValueKind.Array => RequireReference(args[0].Value).Number == 0 ? "Array" : Kind(args[0].Value).ToString(),
            _ => args[0].Value.Kind.ToString(),
        }, readOnly: true),
        _ => null,
    };
}
