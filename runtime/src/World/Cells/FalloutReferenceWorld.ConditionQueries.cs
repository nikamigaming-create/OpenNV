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
    private Func<FalloutFormKey, bool>? _greetingPlayer;
    private Func<FalloutFormKey, bool>? _talking;
    internal void BindTalking(Func<FalloutFormKey, bool> query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_talking is not null && _talking != query)
            throw new InvalidOperationException("Actor talking already has a speech process owner.");
        _talking = query;
    }

    internal void UnbindTalking(Func<FalloutFormKey, bool> query)
    {
        if (_talking == query) _talking = null;
    }

    internal bool IsTalking(FalloutFormKey subject)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_talking ?? throw new NotSupportedException("Talking query has no actor speech process owner."))(subject);
    }

    internal void BindGreetingPlayer(Func<FalloutFormKey, bool> query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_greetingPlayer is not null && _greetingPlayer != query)
            throw new InvalidOperationException("Actor greeting already has a speech process owner.");
        _greetingPlayer = query;
    }

    internal void UnbindGreetingPlayer(Func<FalloutFormKey, bool> query)
    {
        if (_greetingPlayer == query) _greetingPlayer = null;
    }

    internal bool IsGreetingPlayer(FalloutFormKey subject)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_greetingPlayer ?? throw new NotSupportedException("Greeting query has no actor speech process owner."))(subject);
    }

    internal void BindPlayerConditions(Func<FalloutConditionQuery, float> query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playerCondition = query;
    }

    internal float QueryNativeCondition(FalloutFormKey subject, FalloutCondition condition) =>
        QueryNativeCondition(subject, new FalloutConditionQuery(condition));

    internal float QueryMenuMode(FalloutConditionQuery condition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (condition.Argument2 != 0)
            throw new InvalidDataException("MenuMode condition has an unexpected second argument.");
        return (float)Menus.Query(condition.Argument1);
    }

    internal float QueryNativeCondition(FalloutFormKey subject, FalloutConditionQuery condition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (condition.Kind == Function.MenuMode) return QueryMenuMode(condition);
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
