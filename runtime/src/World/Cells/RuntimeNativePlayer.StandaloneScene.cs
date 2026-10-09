using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutReferenceWorld? _standaloneSceneWorld;
    private FalloutStandaloneFirstPersonOwner? _standaloneFirstPersonOwner;
    internal void BindStandalonePlayerScene(FalloutReferenceWorld world)
    {
        if (!world.HasStandalonePlayerScene) return;
        if (_standaloneSceneWorld is not null && !ReferenceEquals(_standaloneSceneWorld, world))
            throw new InvalidOperationException("Player first-person field retains another actual world/process lifetime.");
        _standaloneSceneWorld = world;
        PublishStandaloneFirstPersonScene();
    }
    private void PublishStandaloneFirstPersonScene()
    {
        if (_standaloneSceneWorld is not { } world) return;
        if (_standaloneFirstPersonOwner is not null)
            throw new InvalidOperationException("The previous actual first-person field was not released before replacement.");
        var body = _firstPerson ?? throw new NotSupportedException("Source first-person factory did not return an actual player actor assembly.");
        if (!body.IsFirstPersonSourceRole || _presentationError is not null || !Living())
            throw new NotSupportedException("Actual selected first-person source factory is wrong-role, faulted or unpublished.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Player scene lost its source selection.");
        var path = body.Actor.Appearance.SkeletonPath;
        if (!content.TryRead(path, null, out var bytes, out _))
            throw new FileNotFoundException("First-person scene cannot bind its original source resource.", path);
        var state = world.CampaignStandalonePlayerScene;
        _standaloneFirstPersonOwner = state.PublishFirstPerson(new(body.SourceSceneFactory, state.Process, state.Source.Identity,
            state.Stack, body.GetInstanceId(), path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            "actual-returned-source-first-person-player-assembly/" + GetInstanceId()), Living);
        bool Living() => ReferenceEquals(_standaloneSceneWorld, world) && ReferenceEquals(_firstPerson, body) && _presentationError is null &&
            GodotObject.IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion() &&
            GodotObject.IsInstanceValid(body) && body.IsInsideTree() && !body.IsQueuedForDeletion() &&
            GodotObject.IsInstanceValid(body.Skeleton.Node) && body.Skeleton.Node.IsInsideTree() && !body.Skeleton.Node.IsQueuedForDeletion();
    }
    private void ReleaseStandaloneFirstPersonScene()
    {
        if (_standaloneFirstPersonOwner is not { } owner) return;
        var world = _standaloneSceneWorld ?? throw new InvalidOperationException("Player scene release lost its actual source world.");
        world.RequireStandalonePlayerSceneRetirement();
        world.CampaignStandalonePlayerScene.ReleaseFirstPerson(owner);
        _standaloneFirstPersonOwner = null;
    }
    internal void RetireStandalonePlayerScene()
    {
        ReleaseStandaloneFirstPersonScene();
        _standaloneSceneWorld = null;
    }
    private void RetirePlayerPerceptionAndStandaloneScene()
    {
        var failures = new List<Exception>();
        try { RetirePlayerPerception(); } catch (Exception error) { failures.Add(error); }
        try { RetireStandalonePlayerScene(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Actual player perception/scene retirement retains independent source/native errors.", failures);
    }
    private void ObserveStandaloneViewTransition()
    {
        if (_standaloneSceneWorld is not { } world) return;
        // The current camera/input movement is real presentation behavior. Its
        // Boolean is not the original Player view-byte writer with independent
        // source gates. Preserve the reached producer boundary without aliasing.
        world.CampaignStandalonePlayerScene.RetainUnownedViewTransition(
            "actual-Player-view-input-original-request-byte-and-node-flags-writer-unowned");
    }
}
