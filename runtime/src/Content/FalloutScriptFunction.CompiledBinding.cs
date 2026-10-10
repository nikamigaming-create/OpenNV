namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutScriptFunction
{
    // Equal signatures do not prove equal behavior. Alias admission compares
    // actual delegate method/target identity without executing either getter.
    internal bool SharesInvocation(FalloutScriptFunction other) =>
        ReferenceEquals(this, other) || Arguments.SequenceEqual(other.Arguments) &&
        ReadOnly == other.ReadOnly && Variadic == other.Variadic &&
        Equals(_invoke, other._invoke) && Equals(_invokeValue, other._invokeValue) &&
        Equals(_invokeReference, other._invokeReference);
}
