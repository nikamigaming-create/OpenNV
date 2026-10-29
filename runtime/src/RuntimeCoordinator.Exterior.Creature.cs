using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private readonly HashSet<ExteriorCreaturePreparation> _nativeGridCreaturePreparations = [];
    private readonly Dictionary<FalloutFormKey, RuntimeNativeCreature.PreparedGeometry> _nativeGridCreatureModels = [];

    private sealed class ExteriorCreaturePreparation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Task<RuntimeNativeCreature.PreparedSource> _read;
        private RuntimeNativeCreature.GeometryAssembly? _assembly;
        private readonly RuntimeLiveContentSource _content;
        private bool _disposed;
        internal Task ReadTask => _read;
        internal string NextStage => !_read.IsCompleted ? "worker-pending" : _assembly?.NextStage ?? "skeleton";

        internal ExteriorCreaturePreparation(FalloutCreatureAppearance appearance, RuntimeLiveContentSource content, float units, CancellationToken cancellation)
        {
            _content = content;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var token = _cancellation.Token;
            _read = FalloutContentWorkers.Run(() => RuntimeNativeCreature.PreparedSource.Read(appearance, content, units, token), token);
        }
        internal bool Advance(float units, out RuntimeNativeCreature.PreparedGeometry? geometry)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            geometry = null;
            if (!_read.IsCompleted) return false;
            _assembly ??= new(_read.GetAwaiter().GetResult(), _content, units);
            if (!_assembly.Advance()) return false;
            geometry = _assembly.Take();
            return true;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _cancellation.Cancel(); _cancellation.Dispose(); _assembly?.Dispose();
            _ = _read.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private ExteriorCreaturePreparation? PrepareExteriorCreature(FalloutPlacedReference reference, CancellationToken cancellation)
    {
        var level = _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1;
        var selection = _nativeReferences!.InitializeActorTemplates(reference.FormKey, level, _nativeGlobals);
        if (selection.Absent) return null;
        var appearance = FalloutCreatureAppearanceResolver.Resolve(_nativePluginStack!, reference.Base, reference.FormKey, selection);
        var preparation = new ExteriorCreaturePreparation(appearance, RuntimeLiveContentSource.Current!, _configuration.World.GameUnitsToMeters, cancellation);
        _nativeGridCreaturePreparations.Add(preparation);
        return preparation;
    }
    private void ReleaseExteriorCreature(ExteriorCreaturePreparation? preparation)
    {
        if (preparation is null) return;
        if (!preparation.ReadTask.IsCompleted) _retiredGridReads.Add(preparation.ReadTask);
        _nativeGridCreaturePreparations.Remove(preparation);
        preparation.Dispose();
    }
}
