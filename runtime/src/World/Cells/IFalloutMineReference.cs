using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// The adapter consumes one real reference owner. Authored PGRE and runtime
// PROJ keep different identities while sharing clocks, sounds and damage.
internal interface IFalloutMineReference
{
    FalloutSpatialReferenceId Identity { get; }
    FalloutFormKey? SourceReference { get; }
    FalloutFormKey? Creator { get; }
    FalloutFormKey DamageAttacker { get; }
    FalloutMineState Mine { get; set; }
    bool Taken { get; set; }
    bool Destroyed { get; }
    bool Live { get; }
    double AgeSeconds { get; set; }
    FalloutSoundRandomState SoundRandom { get; }
    FalloutAnimationSoundEvents AnimationSoundEvents { get; }
    FalloutWeaponDamage? Damage { get; }
    bool ActorEligible(FalloutFormKey actor);
    void Take(FalloutPlayerInventory inventory, int level, FalloutGlobalState globals);
}

internal sealed class FalloutPlacedMineReference(FalloutReferenceWorld world,
    FalloutReferenceInstance state, FalloutMineSource source) : IFalloutMineReference
{
    public FalloutSpatialReferenceId Identity => state.Reference;
    public FalloutFormKey? SourceReference => state.Reference;
    public FalloutFormKey? Creator => null;
    public FalloutFormKey DamageAttacker => state.Reference;
    public FalloutMineState Mine { get => world.RequireMine(state.Reference, source); set => state.Mine = value; }
    public bool Taken { get => state.Taken; set => state.Taken = value; }
    public bool Destroyed => state.Destroyed;
    public bool Live => world.CanActivate(state.Reference) && world.IsResident(state.Reference);
    public double AgeSeconds { get; set; } // Authored placements have no launch-age collision exemption.
    public FalloutSoundRandomState SoundRandom => state.SoundRandom;
    public FalloutAnimationSoundEvents AnimationSoundEvents => state.AnimationSoundEvents;
    public FalloutWeaponDamage? Damage => null;
    public bool ActorEligible(FalloutFormKey actor) => world.MineActorEligible(state.Reference, actor);
    public void Take(FalloutPlayerInventory inventory, int level, FalloutGlobalState globals) =>
        world.TakeMine(state.Reference, source, inventory, level, globals);
}
