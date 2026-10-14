using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerOwnerLineageContracts(FalloutMainScriptCallerSource main, string stack)
    {
        using var origin = new PointOfViewFixture(main, stack);
        using var unrelated = new PointOfViewFixture(main, stack);
        var originProjection = BindMovementIntegrationOrientation(origin.Scene, new("PointOfViewFixture.esm", 0x14));
        var unrelatedProjection = BindMovementIntegrationOrientation(unrelated.Scene, new("PointOfViewFixture.esm", 0x14));
        BindOpacity(origin.Scene); BindOpacity(unrelated.Scene);
        var coldOwners = new List<FalloutStandalonePlayerSceneState>();
        try
        {
            var saved = origin.Scene.Capture(); var initial = saved; var foreign = unrelated.Scene.Capture();
            for (var index = 0; index < 3; index++)
            {
                var cold = new FalloutStandalonePlayerSceneState(saved.Source, stack, Guid.NewGuid(),
                    JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!);
                coldOwners.Add(cold); saved = cold.Capture();
                Require(saved.PointOfView!.Lifetimes.Count == index + 2 &&
                    saved.ControlHud!.Process == initial.CapturedProcess && saved.ControlDisplay!.Process == initial.CapturedProcess &&
                    saved.ReferenceOrientation!.CapturedProcess == initial.CapturedProcess && saved.Opacity!.CapturedProcess == initial.CapturedProcess &&
                    JsonSerializer.Serialize(saved.ControlHud) == JsonSerializer.Serialize(initial.ControlHud) &&
                    JsonSerializer.Serialize(saved.ReferenceOrientation) == JsonSerializer.Serialize(initial.ReferenceOrientation) &&
                    JsonSerializer.Serialize(saved.Opacity) == JsonSerializer.Serialize(initial.Opacity),
                    "Repeated cold parent capture discarded validated child lineage or replayed child constructors/fields.");
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                { ControlHud = foreign.ControlHud, ControlDisplay = foreign.ControlDisplay }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ReferenceOrientation = foreign.ReferenceOrientation }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { Opacity = foreign.Opacity }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                { Opacity = saved.Opacity! with { RestoredProcess = foreign.CapturedProcess, RestoredLifetime = Guid.NewGuid() } }));
            }
            Console.WriteLine("OPENNV_PLAYER_CHILD_LINEAGE_PASS threeColdCapturesWithoutRebind=true childFieldsNoReplay=true " +
                "foreignHudRejected=true foreignOrientationRejected=true foreignOpacityRejected=true gameplay=UNEXECUTED");
        }
        finally
        {
            foreach (var cold in coldOwners) cold.Retire();
            origin.Scene.Retire(); unrelated.Scene.Retire();
            originProjection.RetireCurrent(); unrelatedProjection.RetireCurrent();
        }
        static void BindOpacity(FalloutStandalonePlayerSceneState scene)
        {
            var source = FalloutPlayerOpacitySource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
            var owner = new FalloutPlayerOpacityState(source, scene.Stack, scene.Process,
                scene.ReadOpacityFirstPerson, () => null, scene.ReadOpacityScaledTimerBits,
                _ => throw new InvalidOperationException("Lineage capture queried a winning setting without a source call."), scene.RequireOpacityInvocation);
            scene.BindPlayerOpacity(owner, (_, _, _, _) => throw new InvalidOperationException("Lineage capture replayed an actual fade call."));
        }
    }
}
