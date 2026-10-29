using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutConditionRuntime? _sharedConditions;
    private Func<FalloutConditionQuery, float>? _nativeConditionQuery;
    private FalloutConditionRuntime SharedConditions => _sharedConditions ??= new(_aiRecords!, _aiQuests!, _aiWorld)
    {
        Globals = _aiGlobals,
        Hour = () => (_aiClock ?? throw new NotSupportedException("Creature has no game clock.")).Hour,
        RandomPercent = () => _aiState!.SoundRandom.NextBounded(100),
        BaseIdentity = subject => subject == Appearance.Reference ? Appearance.Creature : FalloutReferenceIdentity.Base(_aiRecords!, subject),
        ItemCount = _aiItemCount,
        Sitting = PackageSitting,
        Native = (subject, condition) => subject == Appearance.Reference ? EvaluateNativeCondition(condition) :
            (_aiWorld ?? throw new NotSupportedException("Creature has no other-subject process owner.")).QueryNativeCondition(subject, condition),
        PlayerPlacement = () =>
        {
            if (Combat?.PackagePlayer is not { } player || Combat.PackagePlayerCell is not { } cell) return null;
            var position = player.GlobalPosition / Skeleton.UnitsToMetres;
            return new FalloutReferencePlacement(cell, [position.X, -position.Z, position.Y], [0, 0, 0]);
        },
        UnitsToMetres = () => Skeleton.UnitsToMetres,
    };
}
