using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutConditionRuntime? _sharedConditions;
    private Func<FalloutConditionQuery, float>? _nativeConditionQuery;
    private FalloutConditionRuntime SharedConditions => _sharedConditions ??= new(_aiStack!, _questState!, _aiWorld)
    {
        Globals = _aiGlobals,
        Hour = () => (_aiClock ?? throw new NotSupportedException("AI has no game clock.")).Hour,
        RandomPercent = () => _aiRandom.NextBounded(100),
        BaseIdentity = subject => subject == Appearance.Reference ? Appearance.Npc : FalloutReferenceIdentity.Base(_aiStack!, subject),
        Race = subject => _aiWorld is not null ? _aiWorld.ActorRace(subject) : subject == Appearance.Reference ? Appearance.Race :
            throw new NotSupportedException("AI race has no reference owner."),
        Factions = subject => _aiWorld is not null ? _aiWorld.ActorFactions(subject) : subject == Appearance.Reference ?
            _liveFactions?.Invoke() ?? _factions : throw new NotSupportedException("AI factions have no reference owner."),
        ItemCount = _aiItemCount,
        Sitting = PackageSitting,
        Native = (subject, condition) => subject == Appearance.Reference ? EvaluateOwnAiCondition(condition) :
            (_aiWorld ?? throw new NotSupportedException("AI has no other-subject process owner.")).QueryNativeCondition(subject, condition),
        PlayerPlacement = ConditionPlayerPlacement,
        UnitsToMetres = () => Skeleton.UnitsToMetres,
    };

    private FalloutReferencePlacement? ConditionPlayerPlacement()
    {
        if (Combat?.PackagePlayer is not { } player || Combat.PackagePlayerCell is not { } cell) return null;
        var position = player.GlobalPosition / player.UnitsToMeters;
        return new(cell, [position.X, -position.Z, position.Y], [0, 0, 0]);
    }
}
