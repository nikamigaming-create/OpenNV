using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutNifFile? _meleeBoundSource;
    private FalloutNifBound? _meleeSourceBound;
    internal float CombatMeleeExtent
    {
        get
        {
            var actor = (_thirdPerson ?? throw new NotSupportedException("Player melee extent needs its source body owner.")).Actor;
            var source = actor.Skeleton.Source;
            if (!ReferenceEquals(source, _meleeBoundSource))
            {
                var bound = FalloutActorMeleeBounds.Read(source);
                _meleeSourceBound = bound; _meleeBoundSource = source;
            }
            var current = _meleeSourceBound!;
            return FalloutActorMeleeBounds.MaximumY(current.Center, current.Dimensions,
                actor.Appearance.Height * _sourceScale, actor.Skeleton.UnitsToMetres);
        }
    }
}
