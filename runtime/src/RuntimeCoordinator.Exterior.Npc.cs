using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private readonly HashSet<ExteriorNpcPreparation> _nativeGridNpcPreparations = [];

    private ExteriorNpcPreparation? PrepareExteriorNpc(FalloutPlacedReference reference)
    {
        var level = _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1;
        var selection = _nativeReferences!.InitializeActorTemplates(reference.FormKey, level, _nativeGlobals);
        if (selection.Absent) return null;
        var armor = _nativeReferences.EquippedArmor(reference.FormKey, level, _nativeGlobals);
        var appearance = FalloutNpcAppearanceResolver.Resolve(_nativePluginStack!, reference.Base, reference.FormKey, armor,
            _nativeReferences.ActorAppearanceOverride(reference.FormKey), selection);
        var root = _nativeCurrentCellRoot ?? throw new InvalidOperationException("Actual exterior source caller has no native CELL root.");
        var preparation = new ExteriorNpcPreparation(this, root, appearance, _nativeGridReadCancellation!.Token);
        _nativeGridNpcPreparations.Add(preparation); _nativeGridNpcPublications.Add(preparation.Caller);
        return preparation;
    }

    private void SynchronizeNativeNpcAppearance(RuntimeNativeNpc actor, FalloutCellScene cell) =>
        actor.SynchronizeAppearance(_nativeReferences!, _nativePluginStack!, RuntimeLiveContentSource.Current!,
            (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry,
                _nativePluginStack!, NativeAmbient(cell.Cell)), _nativeOpeningStageDriver?.PlayerLevel ??
                _nativeOpeningRestore?.State.Vitals?.Level ?? 1, _nativeGlobals);
}
