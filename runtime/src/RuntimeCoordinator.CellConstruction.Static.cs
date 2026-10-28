using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private async Task<double> PlaceNativeStaticCellReferenceResponsive(Node3D root, FalloutCellScene cell,
        FalloutPlacedReference reference, string path, FalloutNifFile prepared)
    {
        var previousChildren = root.GetChildCount();
        var started = Stopwatch.GetTimestamp(); double maximumStep = 0;
        RuntimeNativeNifScene? completed = null;
        Node3D? preparedInstance = null;
        try
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Static model source is absent.");
            using var assembly = RuntimeNativeNifMeshBuilder.StaticAssembly.TryCreate(prepared,
                _configuration.World.GameUnitsToMeters, source);
            maximumStep = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (assembly is not null)
            {
                while (true)
                {
                    if (_nativeSessionTransitioning || _retiringNativeSession)
                        throw new OperationCanceledException("Static model construction session retired.");
                    started = Stopwatch.GetTimestamp();
                    var done = assembly.Advance();
                    maximumStep = Math.Max(maximumStep, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    if (done) break;
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                started = Stopwatch.GetTimestamp();
                completed = assembly.Take();
                var prototype = new RuntimeNativeNifPrototype(prepared, _configuration.World.GameUnitsToMeters, completed);
                if (prototype.RequiresFreshOwners)
                    preparedInstance = completed.Root;
                else if (!_nativeNifPrototypes.ContainsKey(path))
                {
                    prototype.Scene.Root.Name = $"Prototype_{reference.Base}";
                    _nativeNifPrototypes.Add(path, prototype);
                    completed = null;
                }
                maximumStep = Math.Max(maximumStep, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            started = Stopwatch.GetTimestamp();
            PlaceNativeCellReference(root, cell, reference, preparedModel: prepared, preparedStaticRoot: preparedInstance);
            maximumStep = Math.Max(maximumStep, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            RejectNativeCellReference(root, reference, previousChildren, error);
        }
        finally
        {
            if (completed is not null && GodotObject.IsInstanceValid(completed.Root) && completed.Root.GetParent() is null)
                completed.Root.Free();
        }
        return maximumStep;
    }
}
