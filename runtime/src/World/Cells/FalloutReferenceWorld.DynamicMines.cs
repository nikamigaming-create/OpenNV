using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<uint, FalloutDynamicMineReference> _dynamicMines = [];
    private readonly Dictionary<uint, IDisposable> _dynamicMineSoundBindings = [];
    internal IReadOnlyCollection<FalloutDynamicMineReference> DynamicMines => _dynamicMines.Values;

    internal void PublishDynamicMine(FalloutDynamicMineReference mine)
    {
        if (!_dynamicMines.TryAdd(mine.Saved.RuntimeId, mine)) throw new InvalidOperationException("Dynamic projectile identity is already published.");
        try
        {
            var id = mine.Saved.RuntimeId;
            _dynamicMineSoundBindings.Add(id, records.SoundVoices.BindRuntimeReference(id,
                () => !_disposed && _dynamicMines.ContainsKey(id) && RuntimeReferences.Require(id).Kind == FalloutRuntimeReferenceKind.Projectile));
        }
        catch { _dynamicMines.Remove(mine.Saved.RuntimeId); throw; }
    }

    internal void RollbackDynamicMine(uint id)
    {
        if (_dynamicMineSoundBindings.Remove(id, out var binding)) binding.Dispose();
        _dynamicMines.Remove(id);
    }

    internal IReadOnlyList<FalloutDynamicMineSnapshot> CaptureDynamicMines() =>
        _dynamicMines.Values.OrderBy(mine => mine.Saved.RuntimeId).Select(mine => mine.Capture()).ToArray();

    internal void RestoreDynamicMines(IReadOnlyList<FalloutDynamicMineSnapshot>? saved)
    {
        if (_dynamicMines.Count != 0) throw new InvalidOperationException("Dynamic mine restore requires a fresh owner.");
        foreach (var item in saved ?? [])
        {
            item.Validate(records, RuntimeReferences);
            PublishDynamicMine(new(this, records, item));
        }
        var owned = RuntimeReferences.Capture().References.Where(reference => reference.Kind == FalloutRuntimeReferenceKind.Projectile).Select(reference => reference.Id).ToHashSet();
        if (!owned.SetEquals(_dynamicMines.Keys)) throw new InvalidDataException("Runtime projectile allocation lost its persistent mine owner.");
    }

    internal void DetonatePlacedExplosives(FalloutFormKey actor)
    {
        FalloutReferenceHitEvents.RequireActor(records, actor);
        foreach (var mine in _dynamicMines.Values.Where(mine => !mine.Taken && mine.Saved.Creator == actor &&
            mine.Mine.Phase == FalloutMinePhase.Armed))
        {
            var source = mine.Saved.Source(records);
            if (source.Projectile.IsRemoteMine) mine.Mine = mine.Mine.CommandDetonation(source, actor);
        }
    }

    internal bool DeployedMineActorEligible(FalloutFormKey creator, FalloutFormKey actor)
    {
        if (creator == actor && records.NumericSettings.IntegerBits("iProjectileMineShooterCanTrigger") <= 0) return false;
        // The shooter setting only permits considering the creator. Native
        // faction reaction still applies to that same actor; a declared
        // self-ally faction therefore excludes its own deployed mine.
        return MineCandidateEligible(actor) && ActorRelation(creator, actor) < 2;
    }
}
