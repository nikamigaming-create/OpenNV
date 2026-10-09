using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private sealed class NativeQueuedActorCaller(Node3D root, FalloutFormKey reference, string owner)
    {
        internal readonly Node3D Root = root;
        internal readonly ulong RootIdentity = root.GetInstanceId();
        internal readonly FalloutFormKey Reference = reference;
        internal readonly string Owner = owner;
        internal RuntimeNativeQueuedActorLoad? Load;
        internal Exception? Failure;
        internal bool CancelRequested;
    }
    private readonly Dictionary<ulong, Dictionary<FalloutFormKey, NativeQueuedActorCaller>> _nativeQueuedActorCallers = [];
    private readonly object _nativeQueuedPresentationEpoch = new();
    private int? _nativeQueuedPresentationThread;
    private void BindActualNativeQueuedCallerThread()
    {
        if (_nativeQueuedPresentationThread is { } thread)
        {
            if (thread != System.Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Native source caller or retirement has a foreign presentation thread.");
        }
        else
        {
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion())
                throw new NotSupportedException("Native source caller has no actual living coordinator/tree epoch.");
            _nativeQueuedPresentationThread = System.Environment.CurrentManagedThreadId;
        }
        if (_nativeReferences?.ActualQueuedReadRegistryConstructed == true)
            _nativeReferences.BindActualQueuedNativePresentationThread(_nativeQueuedPresentationEpoch);
    }
    private NativeQueuedActorCaller BeginNativeQueuedNpc(Node3D root, FalloutNpcAppearance appearance,
        string owner, CancellationToken cancellation)
    {
        BindActualNativeQueuedCallerThread();
        if (_retiringNativeSession || _nativeSessionTransitioning)
            throw new OperationCanceledException("Actual native source caller epoch is retiring.");
        if (!GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion())
            throw new InvalidDataException("Source queued NPC caller has a retired native CELL root.");
        _ = RequireNativeSourceCellAttachment(root);
        var reference = appearance.Reference ?? throw new InvalidDataException("Native queued NPC caller has no actual reference.");
        var world = _nativeReferences ?? throw new InvalidOperationException("Actual queued caller world is absent.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Actual queued caller source is absent.");
        if (!ReferenceEquals(_nativePluginStack?.OwnedSource, source) || world.Get(reference).Base != appearance.Npc)
            throw new InvalidDataException("Native queued actor caller changed the selected source/reference/base identity.");
        var rootIdentity = root.GetInstanceId();
        if (!_nativeQueuedActorCallers.TryGetValue(rootIdentity, out var callers))
            _nativeQueuedActorCallers.Add(rootIdentity, callers = new(FalloutFormKeyComparer.Instance));
        if (callers.ContainsKey(reference))
            throw new NotSupportedException("This actual source/native reference caller already owns entered work or a retained failure.");
        var caller = new NativeQueuedActorCaller(root, reference, owner); callers.Add(reference, caller);
        try { caller.Load = new(world, appearance, source, owner, cancellation); return caller; }
        catch (Exception error) { caller.Failure = error; throw; }
    }
    private FalloutNpcAppearance? ReadNativeQueuedNpcAppearance(FalloutPlacedReference reference)
    {
        var level = _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1;
        var world = _nativeReferences ?? throw new InvalidOperationException("Actual source NPC appearance has no world.");
        var selection = world.InitializeActorTemplates(reference.FormKey, level, _nativeGlobals);
        if (selection.Absent) return null;
        var armor = world.EquippedArmor(reference.FormKey, level, _nativeGlobals);
        return FalloutNpcAppearanceResolver.Resolve(_nativePluginStack!, reference.Base, reference.FormKey, armor,
            world.ActorAppearanceOverride(reference.FormKey), selection);
    }
    private bool AdvanceNativeQueuedNpc(NativeQueuedActorCaller caller, FalloutCellScene cell, out RuntimeNativeNpc? actor)
    {
        actor = null;
        if (caller.Failure is { } failure) throw new InvalidOperationException("Native queued caller retains its original failure.", failure);
        var load = caller.Load ?? throw new InvalidOperationException("Native queued caller constructor did not return.");
        try
        {
            if (!GodotObject.IsInstanceValid(caller.Root) || caller.Root.GetInstanceId() != caller.RootIdentity ||
                caller.Root.IsQueuedForDeletion() || caller.CancelRequested)
                throw new OperationCanceledException("Actual queued native root/caller was cancelled or retired.");
            return load.Advance(_configuration.World.GameUnitsToMeters, (appearance, part, nif, geometry) =>
                NativeNpcMaterial.Resolve(appearance, part, nif, geometry, _nativePluginStack!, NativeAmbient(cell.Cell)), out actor);
        }
        catch (Exception error)
        {
            caller.Failure ??= error;
            try { load.RetainCallerFailure(error); }
            catch (Exception retain) { throw new AggregateException("Actual native caller and failure retention both failed.", error, retain); }
            throw;
        }
    }
    private RuntimeNativeNpc CreateNativeQueuedNpc(Node3D root, FalloutCellScene cell, FalloutPlacedReference reference)
    {
        var appearance = ReadNativeQueuedNpcAppearance(reference) ??
            throw new InvalidDataException("Absent source template arm cannot construct a queued NPC.");
        var caller = BeginNativeQueuedNpc(root, appearance, "actual-synchronous-reference-Load3D", default);
        var load = caller.Load!;
        // Synchronous source callers retain the same real owned-file Task.
        // Responsive construction below yields while that Task/assembly runs.
        try
        {
            load.ReadTask.GetAwaiter().GetResult();
            while (!AdvanceNativeQueuedNpc(caller, cell, out _)) { }
            return load.TransferredActor ?? throw new InvalidOperationException("Actual queued source NPC was not transferred.");
        }
        catch (Exception error) { RetainNativeQueuedCallerFailure(caller, error); throw; }
    }
    private async Task PlaceNativeCellReferenceResponsive(Node3D root, FalloutCellScene cell, FalloutPlacedReference reference)
    {
        if (cell.BaseObjects[reference.Base].Signature != "NPC_" || !_nativeReferences!.IsEnabled(reference.FormKey))
        { PlaceNativeCellReference(root, cell, reference); return; }
        var previousChildren = root.GetChildCount();
        RuntimeNativeNpc? transferred = null;
        NativeQueuedActorCaller? caller = null;
        try
        {
            var appearance = ReadNativeQueuedNpcAppearance(reference);
            if (appearance is null) { PlaceNativeCellReference(root, cell, reference); return; }
            caller = BeginNativeQueuedNpc(root, appearance, "actual-responsive-CELL-reference-Load3D", default);
            while (!AdvanceNativeQueuedNpc(caller, cell, out transferred))
            {
                if (_nativeSessionTransitioning || _retiringNativeSession)
                    throw new OperationCanceledException("Actual responsive native caller was retired before publication.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            PlaceNativeReference(root, cell, reference, preparedNpc: transferred);
            if (transferred is not null && GodotObject.IsInstanceValid(transferred) && transferred.GetParent() is null) transferred.Free();
            CompleteNativeSourceCellReference(root, cell, reference, previousChildren);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            if (caller is not null) RetainNativeQueuedCallerFailure(caller, error);
            if (transferred is not null && GodotObject.IsInstanceValid(transferred) && transferred.GetParent() is null) transferred.Free();
            while (root.GetChildCount() > previousChildren) root.GetChild(previousChildren).Free();
            FailNativeSourceCellReference(root, reference, error, previousChildren);
            _nativeReferenceDivergences[reference.FormKey.ToString()] = MessageNativeQueuedCaller(error);
            GD.PushError($"OPENNV_NATIVE_REFERENCE_DIVERGENCE reference={reference.FormKey} base={reference.Base}: {error.Message}");
            RetireReturnedNativeQueuedActorCallers();
        }
    }
    private static string MessageNativeQueuedCaller(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    private void RetainNativeQueuedCallerFailure(NativeQueuedActorCaller caller, Exception error)
    {
        caller.Failure ??= error;
        caller.Load?.RetainCallerFailure(error);
    }
    private void PublishNativeQueuedActorCallers(Node3D root)
    {
        if (!_nativeQueuedActorCallers.TryGetValue(root.GetInstanceId(), out var callers)) return;
        foreach (var caller in callers.Values.ToArray())
        {
            if (caller.Failure is { } failure) throw new InvalidOperationException("Actual source/native CELL caller retains a failed publication prefix.", failure);
            var load = caller.Load ?? throw new InvalidOperationException("Actual queued native constructor did not return.");
            try
            {
                load.PublicationReturned(); load.Dispose(); ReleaseRetiredExteriorNpcOwner(caller); callers.Remove(caller.Reference);
            }
            catch (Exception error) { RetainNativeQueuedCallerFailure(caller, error); throw; }
        }
        if (callers.Count == 0) _nativeQueuedActorCallers.Remove(root.GetInstanceId());
    }
    private bool HasNativeQueuedActorCallers(ulong root) =>
        _nativeQueuedActorCallers.TryGetValue(root, out var callers) && callers.Count != 0;
    private NativeQueuedActorCaller? FindNativeQueuedActorCaller(Node3D root, FalloutFormKey reference) =>
        _nativeQueuedActorCallers.TryGetValue(root.GetInstanceId(), out var callers) ? callers.GetValueOrDefault(reference) : null;
    private object NativeQueuedActorCallerState => _nativeQueuedActorCallers.Values.SelectMany(value => value.Values).Select(caller => new
    {
        root = caller.RootIdentity, reference = caller.Reference.ToString(), caller.Owner,
        task = caller.Load?.Identity, readReturned = caller.Load?.ReadTask.IsCompleted,
        actor = caller.Load?.TransferredActor is { } actor && GodotObject.IsInstanceValid(actor) ? actor.GetInstanceId() : (ulong?)null,
        caller.CancelRequested, failure = caller.Failure is { } error ? MessageNativeQueuedCaller(error) : null
    }).ToArray();
}
