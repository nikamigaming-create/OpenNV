using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private readonly FalloutFollowCombatBoundary _followCombatBoundary = new();
    internal string? FollowCombatError => _followCombatBoundary.Error;
    private FalloutFollowPackage? FollowCombatSource => _actor switch
    {
        RuntimeNativeNpc npc => npc.FollowCombatSource,
        RuntimeNativeCreature creature => creature.FollowCombatSource,
        _ => null
    };
    private bool HasUnboundFollowCombat => FollowCombatSource?.RequiresDefensiveCombatOwner == true;

    private void RequireFollowCombatPolicy(string reached, bool continuation = false)
    {
        try
        {
            if (continuation) _followCombatBoundary.RequireContinuation(FollowCombatSource, _state, reached);
            else _followCombatBoundary.Require(FollowCombatSource, _state, reached);
        }
        catch (NotSupportedException)
        {
            StopPackageMotion();
            throw;
        }
    }

    internal void RetireFollowCombatPolicy(FalloutFormKey package)
    {
        if (_followCombatBoundary.Retire(package, _state) is not { } retired) return;
        if (_engagementError == retired) { _engagementError = null; _launchRefusalOwnsError = false; }
        if (_assistanceError == retired) _assistanceError = null;
    }
}
