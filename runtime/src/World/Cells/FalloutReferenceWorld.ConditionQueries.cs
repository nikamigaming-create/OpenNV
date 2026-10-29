using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceInstance
{
    internal Func<FalloutConditionQuery, float>? QueryNativeCondition { get; set; }
}

internal sealed partial class FalloutReferenceWorld
{
    private Func<FalloutConditionQuery, float>? _playerCondition;
    internal void BindPlayerConditions(Func<FalloutConditionQuery, float> query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playerCondition = query;
    }

    internal float QueryNativeCondition(FalloutFormKey subject, FalloutCondition condition) =>
        QueryNativeCondition(subject, new FalloutConditionQuery(condition));

    internal float QueryNativeCondition(FalloutFormKey subject, FalloutConditionQuery condition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var query = subject == records.RuntimeFormKey(0x14) ? _playerCondition : Get(subject).QueryNativeCondition;
        var owner = condition.Kind switch
        {
            Function.GetDead or Function.GetHealthPercentage => "authoritative vitals",
            Function.GetInCell => "live cell",
            _ => "authoritative native process",
        };
        return (query ?? throw new NotSupportedException(
            $"Condition {condition.Function} has no {owner} owner for {subject}."))(condition with
            { Declaration = condition.Declaration with { RunOn = 0, Reference = 0, Flags = (byte)(condition.Declaration.Flags & ~2) } });
    }
}
