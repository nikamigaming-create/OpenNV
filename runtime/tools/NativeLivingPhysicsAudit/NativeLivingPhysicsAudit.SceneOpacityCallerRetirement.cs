using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

public partial class NativeLivingPhysicsAudit
{
    private static void ExerciseSceneOpacityCallerRetirement()
    {
        var physical = FalloutNifPhysicalModel.Read(FalloutNifFile.Read(FalloutLivingPhysicsFixture.Create()));
        var sourceModel = new FalloutActorSourceModel(FixtureAppearance("authored-retirement.nif"), physical, []);
        var logical = sourceModel.ConstructLogicalScene();
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3,
            FalloutPhysicalConstructorInputs.ReviewedEngineSha256,
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(main), new('2', 64), Guid.NewGuid());
        var root = new Node3D();
        var owner = Publish(sourceModel);
        using var nativeWorld = new World3D();
        var backend = new RuntimeNativeLivingPhysicsBackend("authored-retirement-world", () => nativeWorld.Space, .02f);
        using var model = FalloutLivingActorModel.Construct("authored-retirement-model", sourceModel, Inputs(), backend, SourceBodyFactory(physical));
        using var world = new FalloutLivingPhysicalWorld(backend);
        using var clock = new FalloutNativePenetrationClock(FalloutPenetrationClockSource.ForExecutable(main.EngineSha256));
        clock.Initialize();
        foreach (var body in model.Bodies) world.AddBody(body);
        foreach (var constraint in model.Constraints) world.AddConstraint(constraint);
        var nativeBodies = model.Bodies.Select(body => body.Backend).ToArray();
        using var program = new Shader { Code = "shader_type spatial; void fragment() { ALBEDO = vec3(1.0); }" };
        using var material = new ShaderMaterial { Shader = program, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
        var borrower = new NativeNifSceneAlphaMaterial(material);
        var cleared = false;
        try
        {
            borrower.Borrow(0x3f000000); material.Dispose();
            AggregateException? failure = null;
            try
            {
                NativeNifSceneRetirement.Drain("authored-actual-Player-caller-retirement",
                    [() => RuntimeNativePlayer.ReleaseStandaloneFirstPersonOwnership(scene, owner,
                        model.Dispose, RetireFailedNativeScene, () => cleared = true), world.Dispose, clock.Dispose]);
            }
            catch (AggregateException error) { failure = error; }
            Require(failure?.Flatten().InnerExceptions is { Count: 1 } failures && failures[0] is ObjectDisposedException &&
                cleared && scene.Capture().FirstPerson is { Released: not null } && logical.SaveBlocker == "source-Actor-logical-scene-retired" &&
                nativeBodies.All(body => !body.HasPhysicalObject),
                "A real native borrower failure skipped actual source-field, logical-scene or physical-body retirement.");
            Reject(world.RequireCurrent); Reject(() => clock.ReadCounterTicks());

            // This is an authored publication/ownership fixture. It tests the
            // real source field and native failure, not an assembled Player.
            var replacement = new FalloutActorSourceModel(FixtureAppearance("authored-retirement.nif"), physical, []);
            var replacementLogical = replacement.ConstructLogicalScene();
            var replacementOwner = Publish(replacement); cleared = false;
            try
            {
                AggregateException? rejected = null;
                try
                {
                    RuntimeNativePlayer.ReleaseStandaloneFirstPersonOwnership(scene,
                    replacementOwner with { Identity = Guid.NewGuid() }, model.Dispose, replacementLogical.Retire, () => cleared = true);
                }
                catch (AggregateException error) { rejected = error; }
                Require(rejected?.Flatten().InnerExceptions is { Count: 1 } errors && errors[0] is InvalidOperationException &&
                    !cleared && scene.Capture().FirstPerson is { Released: null } retained && retained.Identity == replacementOwner.Identity,
                    "Rejected actual field release cleared a still-owned source publication.");
                RuntimeNativePlayer.ReleaseStandaloneFirstPersonOwnership(scene, replacementOwner,
                    model.Dispose, replacementLogical.Retire, () => cleared = true);
                Require(cleared && scene.Capture().FirstPerson is { Released: not null },
                    "A failed source release lost genuine retryability with its original owner.");
            }
            finally { replacementLogical.Retire(); }
        }
        finally
        {
            try
            {
                borrower.Retire(); logical.Retire();
                if (scene.Capture().FirstPerson is { Released: null } stillOwned) scene.ReleaseFirstPerson(stillOwned);
                scene.Retire();
            }
            finally { root.Free(); }
        }

        FalloutStandaloneFirstPersonOwner Publish(FalloutActorSourceModel source) => scene.PublishFirstPerson(
            new(source.Factory, scene.Process, scene.Source.Identity, scene.Stack, root.GetInstanceId(),
                source.Appearance.SkeletonPath, physical.Source.Sha256, "authored-real-field-retirement"),
            () => GodotObject.IsInstanceValid(root));
        void RetireFailedNativeScene()
        {
            try { borrower.Borrow(0x3e800000); }
            finally { NativeNifSceneRetirement.Drain("authored-native-and-logical-scene", [borrower.Retire, logical.Retire]); }
        }
    }
}
