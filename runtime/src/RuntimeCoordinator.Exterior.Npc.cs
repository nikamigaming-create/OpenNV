using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private readonly HashSet<ExteriorNpcPreparation> _nativeGridNpcPreparations = [];

    private sealed class ExteriorNpcPreparation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Task<FalloutNpcPreparedGeometry> _read;
        private RuntimeNativeNpc.Assembly? _assembly;
        internal FalloutNpcAppearance Appearance { get; }
        internal Task ReadTask => _read;

        internal ExteriorNpcPreparation(FalloutNpcAppearance appearance, RuntimeLiveContentSource source, CancellationToken cancellation)
        {
            Appearance = appearance;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var token = _cancellation.Token;
            _read = FalloutContentWorkers.Run(() => FalloutNpcPreparedGeometry.Read(appearance, source, token), token);
        }

        internal bool Advance(RuntimeCoordinator owner, FalloutCellScene cell, out RuntimeNativeNpc? actor)
        {
            actor = null;
            if (!_read.IsCompleted) return false;
            _assembly ??= new(_read.GetAwaiter().GetResult(), RuntimeLiveContentSource.Current!,
                owner._configuration.World.GameUnitsToMeters, (appearance, part, nif, geometry) =>
                    NativeNpcMaterial.Resolve(appearance, part, nif, geometry, owner._nativePluginStack!, owner.NativeAmbient(cell.Cell)));
            if (!_assembly.Advance()) return false;
            actor = _assembly.Take();
            return true;
        }

        public void Dispose()
        {
            _cancellation.Cancel(); _cancellation.Dispose();
            _assembly?.Dispose();
            _ = _read.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private ExteriorNpcPreparation? PrepareExteriorNpc(FalloutPlacedReference reference)
    {
        var level = _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1;
        var selection = _nativeReferences!.InitializeActorTemplates(reference.FormKey, level, _nativeGlobals);
        if (selection.Absent) return null;
        var armor = _nativeReferences.EquippedArmor(reference.FormKey, level, _nativeGlobals);
        var appearance = FalloutNpcAppearanceResolver.Resolve(_nativePluginStack!, reference.Base, reference.FormKey, armor, selection: selection);
        var preparation = new ExteriorNpcPreparation(appearance, RuntimeLiveContentSource.Current!, _nativeGridReadCancellation!.Token);
        _nativeGridNpcPreparations.Add(preparation);
        return preparation;
    }

    private void ReleaseExteriorNpc(ExteriorNpcPreparation? preparation)
    {
        if (preparation is null) return;
        _nativeGridNpcPreparations.Remove(preparation);
        preparation.Dispose();
    }
}
