namespace OpenNV.Runtime.Content;

// CTDA raw FormIDs use the declaring plugin's namespace. Script arguments have
// already passed compiled binding/value resolution and must not be readjusted.
internal readonly record struct FalloutConditionQuery(FalloutCondition Declaration, FalloutFormKey? BoundForm = null,
    FalloutScriptValue? BoundSecondForm = null)
{
    internal FalloutPluginRecord Owner => Declaration.Owner;
    internal ushort Function => Declaration.Function;
    internal Definitions.FalloutConditionFunction Kind => Declaration.Kind;
    internal uint Argument1 => Declaration.Argument1;
    internal uint Argument2 => Declaration.Argument2;
    internal uint RunOn => Declaration.SubjectRunOn;
    internal FalloutFormKey FormArgument1 => BoundForm ?? Declaration.FormArgument1;
    // Resolve only when an active selector actually consumes a form. Script
    // values are runtime identities; raw CTDA operands use the declaring plugin.
    internal FalloutFormKey FormArgument2(FalloutPluginStack records) => BoundSecondForm is { } bound
        ? bound.FormKey(records) : Owner.Plugin.AdjustFormId(Argument2);
    internal bool IsNullFormArgument1(FalloutPluginStack records) =>
        BoundForm is { } bound ? records.RuntimeFormId(bound) == 0 : Argument1 == 0;
}
