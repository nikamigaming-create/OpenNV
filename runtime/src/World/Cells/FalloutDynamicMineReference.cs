using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed class FalloutDynamicMineReference : IFalloutMineReference
{
    private readonly FalloutReferenceWorld _world;
    private readonly FalloutPluginStack _records;
    internal FalloutDynamicMineSnapshot Saved { get; private set; }
    internal Func<FalloutDynamicMineSnapshot>? CapturePhysical { get; set; }
    internal bool Resident { get; set; }
    public FalloutSpatialReferenceId Identity => FalloutSpatialReferenceId.Runtime(Saved.RuntimeId);
    public FalloutFormKey? SourceReference => null;
    public FalloutFormKey? Creator => Saved.Creator;
    public FalloutFormKey DamageAttacker => Saved.Creator;
    public FalloutMineState Mine { get => Saved.Mine; set => Saved = Saved with { Mine = value }; }
    public bool Taken
    {
        get => Saved.Taken;
        set
        {
            if (!value || Saved.Taken) throw new InvalidOperationException("A retired projectile cannot be reused.");
            _world.RuntimeReferences.Retire(Saved.RuntimeId);
            Saved = Saved with { Taken = true };
        }
    }
    public bool Destroyed => false;
    public bool Live => Resident && !Taken;
    public double AgeSeconds { get => Saved.AgeSeconds; set => Saved = Saved with { AgeSeconds = value }; }
    public FalloutSoundRandomState SoundRandom { get; }
    public FalloutAnimationSoundEvents AnimationSoundEvents { get; }
    public FalloutWeaponDamage? Damage => Saved.Damage;

    internal FalloutDynamicMineReference(FalloutReferenceWorld world, FalloutPluginStack records, FalloutDynamicMineSnapshot saved)
    {
        _world = world; _records = records; Saved = saved.Copy();
        SoundRandom = new(saved.SoundRandomState);
        AnimationSoundEvents = new(Identity, () => Live);
        AnimationSoundEvents.Restore(saved.Sounds, records);
    }

    public bool ActorEligible(FalloutFormKey actor) => _world.DeployedMineActorEligible(Saved.Creator, actor);
    public void Take(FalloutPlayerInventory inventory, int level, FalloutGlobalState globals)
    {
        var source = Saved.Source(_records);
        if (!Live || !source.Projectile.CanBePickedUp || Mine.Phase == FalloutMinePhase.Detonated ||
            source.Projectile.CanBeDisabled && Mine.Phase != FalloutMinePhase.Disabled || Mine.Error is not null)
            throw new InvalidOperationException("Dynamic mine is not available for pickup.");
        FalloutReferenceWorld.RequirePlayerItemAcquisition(_records, _records.GetEffective(Saved.Weapon), Saved.Creator);
        var before = Saved;
        using var lease = new FalloutInventoryChangeLease(() =>
        {
            if (!Live || Saved != before) throw new InvalidOperationException("Dynamic mine changed during pickup.");
            // Retirement follows the successful inventory transaction, which is
            // the last fallible operation. No new inventory identity is minted.
            Saved = Saved with { Taken = true };
        }, () => Saved = before, () => _world.RuntimeReferences.Retire(Saved.RuntimeId));
        inventory.AddWithPreparedOwner(_records, Saved.Weapon, 1, level, false, lease, globals, Saved.Item);
    }

    internal FalloutDynamicMineSnapshot Capture()
    {
        var physical = CapturePhysical?.Invoke() ?? Saved;
        Saved = physical with
        {
            Mine = Mine,
            Taken = Taken,
            AgeSeconds = AgeSeconds,
            SoundRandomState = SoundRandom.State,
            Sounds = AnimationSoundEvents.Capture()
        };
        Saved.Validate(_records, _world.RuntimeReferences);
        return Saved.Copy();
    }
}
