using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    internal bool FindFoodQuotaSatisfied(FalloutFindFoodPackage source)
    {
        var inventory = _state.Inventory ?? throw new NotSupportedException("Find Food has no authoritative actor inventory.");
        return source.QuotaSatisfied(inventory.Contents, _world.IsActorFood);
    }

    internal void RequireEmptyFindFoodSearch(FalloutFindFoodPackage source)
    {
        var scan = _world.FindFoodSources(_state.Reference, source.Declaration, _skeleton.UnitsToMetres);
        // The native Food-category search excludes player-held inventory;
        // its candidate classifier independently rejects Creature holders.
        // Unknown Character/container inventory is never an empty result.
        scan.RequireEmpty(reference => _records.RuntimeFormId(reference) == 0x14 ||
            _records.GetEffective(_world.Get(reference).Base).Signature == "CREA");
    }

    internal void SetFindFoodProgress(FalloutFindFoodProgress progress)
    {
        progress.Validate();
        var motion = _state.PackageMotion ?? throw new InvalidOperationException("Find Food has no native motion observation.");
        _state.PackageMotion = motion with
        { FindFood = progress, Wander = progress.Phase == FalloutFindFoodPhase.Wander ? motion.Wander : null };
    }

    internal bool FindFoodWanderCanRetry => EatWanderCanRetry;

    internal void AdvanceFindFoodWander(FalloutPluginRecord package, FalloutFindFoodPackage source,
        FalloutFindFoodProgress progress, double delta)
    {
        var context = _context ?? throw new NotSupportedException("Find Food Wander has no gameplay context.");
        var location = source.Declaration.Location(_records, _world, _state.Reference);
        var request = _state.PackageMotion?.Package == package.FormKey ? _state.PackageMotion.Wander : null;
        if (request is null || request.Arrived)
            request = _world.BeginPackageWander(_state.Reference,
                new Vector3(location.Position[0], location.Position[1], location.Position[2]),
                source.Declaration.Source.LocationRadius, _records.NumericSettings, context.Globals, _skeleton.UnitsToMetres);
        request = AdvancePackageWander(package, request, source.Running, source.WeaponDrawn, delta);
        _state.PackageMotion = _state.PackageMotion! with { Wander = request, FindFood = progress };
    }
}
