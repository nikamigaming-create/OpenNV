namespace OpenNV.Runtime.Content;

// CTDA raw FormIDs use the declaring plugin's namespace. Script arguments have
// already passed compiled binding/value resolution and must not be readjusted.
internal readonly record struct FalloutConditionQuery(FalloutCondition Declaration, FalloutFormKey? BoundForm = null)
{
    internal FalloutPluginRecord Owner => Declaration.Owner;
    internal ushort Function => Declaration.Function;
    internal Definitions.FalloutConditionFunction Kind => Declaration.Kind;
    internal uint Argument1 => Declaration.Argument1;
    internal uint Argument2 => Declaration.Argument2;
    internal uint RunOn => Declaration.SubjectRunOn;
    internal FalloutFormKey FormArgument1 => BoundForm ?? Declaration.FormArgument1;
}
