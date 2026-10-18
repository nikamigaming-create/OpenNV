using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private async Task<double> PlaceNativeCellReferenceResponsive(Node3D root, FalloutCellScene cell,
        FalloutPlacedReference reference, FalloutNifFile? preparedModel = null)
    {
        var started = Stopwatch.GetTimestamp();
        if (DisplayServer.GetName() == "headless" ||
            !cell.BaseObjects.TryGetValue(reference.Base, out var source) || source.Signature != "NPC_" ||
            !_nativeReferences!.IsEnabled(reference.FormKey))
        {
            PlaceNativeCellReference(root, cell, reference, preparedModel: preparedModel);
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        var previousChildren = root.GetChildCount();
        ExteriorNpcPreparation? preparation = null;
        RuntimeNativeNpc? actor = null;
        double maximumStep = 0;
        try
        {
            // Use the streaming body's worker decode and incremental native
            // assembly for initial loads and doors as well. No partial actor
            // enters the scene or receives gameplay/audio callbacks.
            preparation = PrepareExteriorNpc(reference);
            if (preparation is not null)
                GD.Print($"OPENNV_NATIVE_NPC_PREPARING reference={reference.FormKey} source=bounded-worker detached=true");
            maximumStep = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            while (preparation is not null)
            {
                if (_nativeSessionTransitioning || _retiringNativeSession)
                    throw new OperationCanceledException("NPC construction session retired.");
                var armor = _nativeReferences.EquippedArmor(reference.FormKey,
                    _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1, _nativeGlobals);
                if (!armor.SequenceEqual(preparation.Appearance.EquippedArmor))
                {
                    await ReleaseLoadingNpcPreparation(preparation);
                    preparation = PrepareExteriorNpc(reference);
                    continue;
                }
                started = Stopwatch.GetTimestamp();
                var complete = preparation.Advance(this, cell, out actor);
                maximumStep = Math.Max(maximumStep, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (complete) break;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            started = Stopwatch.GetTimestamp();
            PlaceNativeReference(root, cell, reference, preparedNpc: actor);
            maximumStep = Math.Max(maximumStep, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            RejectNativeCellReference(root, reference, previousChildren, error);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(actor) && actor!.GetParent() is null) actor.Free();
            await ReleaseLoadingNpcPreparation(preparation);
        }
        return maximumStep;
    }

    private async Task ReleaseLoadingNpcPreparation(ExteriorNpcPreparation? preparation)
    {
        ReleaseExteriorNpc(preparation);
        if (preparation is null) return;
        // Keep the loading task alive until cancelled source reads finish.
        // Session retirement awaits that task before disposing source handles.
        try { await preparation.ReadTask; }
        catch (Exception) when (preparation.ReadTask.IsCanceled || preparation.ReadTask.IsFaulted) { }
    }

    private void RejectNativeCellReference(Node3D root, FalloutPlacedReference reference, int previousChildren,
        Exception error)
    {
        while (root.GetChildCount() > previousChildren) root.GetChild(previousChildren).Free();
        _nativeReferenceDivergences[reference.FormKey.ToString()] = error.Message;
        GD.PushError($"OPENNV_NATIVE_REFERENCE_DIVERGENCE reference={reference.FormKey} base={reference.Base}: {error.Message}");
    }

    private async Task AddExteriorLandscapeResponsive(Node3D root, FalloutExteriorGridScene grid)
    {
        if (DisplayServer.GetName() == "headless") { AddExteriorLandscape(root, grid); return; }
        var content = RuntimeLiveContentSource.Current!;
        var records = _nativePluginStack!;
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var pending = new Queue<Task<FalloutLandscapeTransport>>();
        var next = 0;
        var total = Stopwatch.GetTimestamp();
        var slice = total;
        double maximumCellMilliseconds = 0;
        try
        {
            while (next < grid.Cells.Count || pending.Count != 0)
            {
                if (_nativeSessionTransitioning || _retiringNativeSession)
                    throw new OperationCanceledException("Landscape construction session retired.");
                while (next < grid.Cells.Count && pending.Count < FalloutContentWorkers.Concurrency)
                {
                    var cell = grid.Cells[next++];
                    pending.Enqueue(FalloutContentWorkers.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        var landscape = FalloutLandscapeTransportResolver.ResolveCell(records, cell, grid.PersistentCell);
                        foreach (var path in landscape.Textures.Values.SelectMany(texture =>
                            new[] { texture.DiffusePath, texture.NormalPath }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            token.ThrowIfCancellationRequested();
                            _ = content.TryRead(path, null, out _, out _);
                        }
                        return landscape;
                    }, token));
                }
                var source = await pending.Dequeue();
                if (_nativeSessionTransitioning || _retiringNativeSession)
                    throw new OperationCanceledException("Landscape construction session retired.");
                var started = Stopwatch.GetTimestamp();
                root.AddChild(RuntimeNativeLandscapeTransportBuilder.Build(source,
                    _configuration.World.GameUnitsToMeters, _nativeLandscapeTextures));
                maximumCellMilliseconds = Math.Max(maximumCellMilliseconds, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (Stopwatch.GetElapsedTime(slice).TotalMilliseconds < 4) continue;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                slice = Stopwatch.GetTimestamp();
            }
            if (_nativeSessionTransitioning || _retiringNativeSession)
                throw new OperationCanceledException("Landscape construction session retired.");
            CompleteExteriorLandscape(root, grid);
            GD.Print($"OPENNV_NATIVE_LAND_BUILD cells={grid.Cells.Count} elapsedMs={Stopwatch.GetElapsedTime(total).TotalMilliseconds:F1} " +
                $"maxCellMs={maximumCellMilliseconds:F1} sourceReads=bounded-workers nativePublication=main-thread");
        }
        finally
        {
            cancellation.Cancel();
            // Source handles cannot retire while an abandoned read still owns
            // them. Only the already rejected/cancelled pending work is drained.
            try { await Task.WhenAll(pending); }
            catch (Exception) when (cancellation.IsCancellationRequested) { }
        }
    }
}
