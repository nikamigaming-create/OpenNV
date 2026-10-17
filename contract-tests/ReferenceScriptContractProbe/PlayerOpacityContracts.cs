using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class PlayerOpacityContracts
{
    internal static void Run()
    {
        FadeBranches(); LogicalScene(); RealProcessAndFailedChild(); PresentationShaderContracts();
        Console.WriteLine("OPENNV_PLAYER_OPACITY_CONTRACT_PASS actualMutableScene=true orderedSharedShaderStores=true separateMaterialAlpha=true sourceFadeStores=true failedChildPrefix=true coldFresh=true gameplay=UNEXECUTED");
    }
    private static void FadeBranches()
    {
        var fields = FalloutActorTeleportFadeFields.Constructed; FalloutActorTeleportFadeTrace? trace = null;
        var consumers = new FadeConsumers { Timer = Bits(1), Seconds = Bits(1) };
        Run();
        Require(fields.State == 1 && fields.FadeBits == Bits(1) && trace is { Result: false, OpacityReturned: true },
            "Exact fade equality expired a state before the source strict comparison.");
        Run(); Require(fields.State == 0 && fields.FadeBits == Bits(1) && trace!.Result == false, "Positive source fade overshoot did not clamp/store state zero before callback.");
        var reads = consumers.TimerReads; var opacity = consumers.OpacityCalls;
        Run(); Require(consumers.TimerReads == reads && consumers.OpacityCalls == opacity, "State-zero fade read a timer or repeated an equal-alpha callback.");
        fields = new(0, 0x7fc00001, null, 1); Run();
        Require(fields.FadeBits == Bits(1) && consumers.OpacityCalls == opacity + 1, "Source unordered zero-state arm did not commit one before opacity.");
        fields = new(2, Bits(.25f), new("OpacityFixture.esm", 12), 1); consumers.Timer = Bits(.5f); Run();
        Require(fields is { State: 2, FadeBits: 0, Target: null } && trace!.Result == false, "Fade-out lost separate strict-negative clamp/target stores.");
        fields = new(4, 0, new("OpacityFixture.esm", 13), 1);
        Reject(Run);
        Require(fields is { State: 4, FadeBits: 0x3f800000, Target: null } && trace is
            { Step: FalloutActorTeleportFadeStep.Failed, FailedAt: FalloutActorTeleportFadeStep.TargetChild, OpacityReturned: false },
            "A missing teleport target child erased preceding stores or reported opacity/state return.");
        fields = new(4, 0, null, 1); Run();
        Require(fields.State == 0 && trace is { Result: true, OpacityReturned: false }, "Player state-four null-target arm called common opacity or failed its early return.");
        fields = new(6, Bits(.1f), null, 1); Reject(Run);
        Require(fields.FadeBits == 0 && trace!.FailedAt == FalloutActorTeleportFadeStep.ActorChild, "Actor disable refusal erased its source clamp prefix.");
        void Run()
        {
            var before = fields;
            consumers.Invocation = Guid.NewGuid();
            try { FalloutActorTeleportFade.Execute(consumers.Invocation, true, consumers, () => fields, value => fields = value, value => trace = value); }
            finally { FalloutActorTeleportFade.ValidateTransition(before, fields, trace!, enginePlayer: true); }
        }
    }
    private static void LogicalScene()
    {
        var model = Model(); var scene = model.ConstructLogicalScene();
        Require(scene.Objects.Count == 5 && scene.Root.Children.Count == 4 && scene.Root.Children[1] is null &&
            scene.Root.Bound is { Radius: 0x3f800000 } && scene.Properties.Count == 3,
            "Actual authored NIF constructors lost geometry, nullable slots, sphere or original shared properties.");
        var alpha = scene.SetAlpha(Guid.NewGuid(), Bits(.25f));
        Require(alpha.Stores.SequenceEqual(new FalloutNifSceneKey[] { new(0, 5), new(0, 6), new(0, 5) }) &&
            scene.RequireProperty(new(0, 5)).Capture() is { ShaderKind: 7, ShaderAlphaBits: 0x3e800000, ShaderStores: 2 } &&
            scene.RequireProperty(new(0, 7)).Capture() is { MaterialAlphaBits: 0x3ec00000, MaterialStores: 0 },
            "Scene alpha changed traversal order, aliased serialized ShaderType/material alpha, or lost repeated writes through shared properties.");
        scene.PublishLocalPose(new(0, 2), FalloutNifSceneMath.Identity with { Translation = new(3, 0, 0) }); scene.UpdateWorldAndBounds();
        Require(scene.Root.Bound!.Sphere.Radius == 2.5f && scene.Root.Bound.Sphere.Center.X == 1.5f,
            "Current source pose did not advance real geometry and parent sphere state.");
        var beforeZero = scene.RequireProperty(new(0, 5)).ShaderStores;
        var retained = scene.Root.Bound;
        scene.Root.Bound = retained with { Radius = 0x80000000 };
        Require(scene.SetAlpha(Guid.NewGuid(), Bits(.75f)).Stores.Count == 0 && scene.RequireProperty(new(0, 5)).ShaderStores == beforeZero,
            "Negative zero did not take the source ordered-zero bound return.");
        scene.Root.Bound = retained;
        var saved = RoundTrip(scene.Capture()); FalloutActorLogicalScene.Validate(saved);
        var cold = Model().ConstructLogicalScene(saved);
        Require(cold.Factory != scene.Factory && cold.Lifetime != scene.Lifetime && cold.Root.Bound!.Identity != scene.Root.Bound!.Identity &&
            cold.Root.Bound.Radius == scene.Root.Bound.Radius && cold.RequireProperty(new(0, 5)).ShaderStores == beforeZero &&
            cold.Capture().Objects.Select(node => node.PoseStores).SequenceEqual(saved.Objects.Select(node => node.PoseStores)),
            "Cold logical construction replayed source pose/bounds/shader stores or reused an old factory.");
        var oldMaterial = cold.RequireProperty(new(0, 7)).MaterialAlphaBits;
        cold.SetAlpha(Guid.NewGuid(), 0x7fc00003);
        Require(cold.RequireProperty(new(0, 5)).ShaderAlphaBits == 0x7fc00003 && cold.RequireProperty(new(0, 7)).MaterialAlphaBits == oldMaterial,
            "Raw unordered shader alpha was clamped or replaced material-animation alpha.");
        Reject(() => Model().ConstructLogicalScene(saved with
        { Objects = saved.Objects.Select(node => node.Key == saved.Root ? node with { Children = [new(0, 4), new(0, 4)] } : node).ToArray() }));
        Reject(() => Model().ConstructLogicalScene(saved with
        { Properties = saved.Properties.Select(property => property.Key == new FalloutNifSceneKey(0, 5) ? property with { ShaderKind = 123 } : property).ToArray() }));
        var blend = new FalloutNifSourcePoseBlend(1); blend.Select(0, 2); blend.Add(0, 1, 1, new(new(100, 0, 0), null, null));
        blend.Add(0, 2, 1, new(new(2, 0, 0), new(1, 0, 0, 0), 1)); FalloutNifAnimationSample result = default;
        blend.Publish((_, sample) => result = sample);
        Require(result.Translation!.Value.X == 2 && result.Rotation!.Value == new FalloutNifQuaternion(1, 0, 0, 0),
            "C# source blend imported coordinates, lost priority or reordered source quaternion components.");
        cold.Retire(); scene.Retire(); Reject(() => cold.SetAlpha(Guid.NewGuid(), Bits(1)));
    }
    private static void RealProcessAndFailedChild()
    {
        var source = Source(); var stack = new string('2', 64); var main = Guid.NewGuid(); var process = Guid.NewGuid();
        var actor = new FalloutFormKey("OpacityFixture.esm", 0x14);
        FalloutCombatActorIdentity Identity(FalloutFormKey key) => new(key, new("OpacityFixture.esm", 7), "ENGINE_PLAYER", 0,
            source.Controls.Main.EngineSha256, "NPC_", 0, new('4', 64), true);
        using var common = new FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration.ForExecutable(source.Controls.Main.EngineSha256),
            stack, Identity, _ => throw new InvalidOperationException("Opacity duplicated gameplay."), _ => throw new InvalidOperationException("Opacity requested a presentation body."));
        common.Construct(actor, 1, FalloutDetectionProcessLevel.High);
        var model = Model(); var scene = model.ConstructLogicalScene();
        var first = new FalloutStandaloneFirstPersonOwner(Guid.NewGuid(), 1,
            new(scene.Factory, process, FalloutStandalonePlayerSceneSource.Read(source.Controls.Main).Identity,
                stack, 1, scene.SkeletonPath, scene.SkeletonSha256, "authored-real-source-graph"), null);
        var opacity = new FalloutPlayerOpacityState(source, stack, process, () => first, () => scene, () => Bits(.25f), _ => Bits(1),
            (call, ordinal) => Require(call == main && ordinal == 1, "Opacity changed authored invocation."));
        Require(!opacity.SkipTeleportUpdate(() => throw new InvalidOperationException("Zero gates queried state.")), "Default source gates skipped fade.");
        var invocation = Guid.NewGuid();
        var call = common.UpdateTeleportFade(source, actor, 1, invocation, (_, read, store) => opacity.BindTeleportConsumers(main, 1, invocation, read, store));
        // Teleport retains this query header but consumes its own fade fields.
        // Independent process pointers cannot become null-only prerequisites.
        var observedPointers = call with { Before = call.Before with
        {
            AnimationContext = Guid.NewGuid(), IdlePrimaryPackage = Guid.NewGuid(), ActorValueCache = Guid.NewGuid(),
            WeaponEntry = new(Guid.NewGuid(), actor, 1)
        } };
        FalloutProcessTeleportFadeReceipt.Validate(observedPointers, stack, source.Controls.Main.EngineSha256, true);
        var pointerHistory = JsonSerializer.Deserialize<FalloutProcessTeleportFadeUpdate>(JsonSerializer.Serialize(observedPointers))!;
        FalloutProcessTeleportFadeReceipt.Validate(pointerHistory, stack, source.Controls.Main.EngineSha256, true);
        Require(pointerHistory.Before == observedPointers.Before, "Teleport history erased independent current process pointers.");
        Reject(() => FalloutProcessTeleportFadeReceipt.Validate(pointerHistory with
        { Before = pointerHistory.Before with { AnimationContext = Guid.Empty } }, stack, source.Controls.Main.EngineSha256, true));
        Require(call.AfterFields.FadeBits == Bits(.25f) && common.ReadOpacity(actor, 1).Opacity is { EffectiveBits: 0x3e800000, Stores: 1 } &&
            opacity.Capture().Call is { EffectiveStored: true, Step: FalloutPlayerOpacityStep.Returned, Scene.Stores.Count: 3 },
            "Real current High fields or source model were replaced with entry metadata/opaque one.");
        var saved = RoundTrip(common.Capture()); FalloutActorProcessCommonState.Validate(saved);
        var savedEntry = saved.Current.Single();
        Reject(() => FalloutActorProcessCommonState.Validate(saved with { Current = [savedEntry with
        { SceneFields = savedEntry.SceneFields! with { TeleportCall = savedEntry.SceneFields.TeleportCall! with
            { Before = savedEntry.SceneFields.TeleportCall.Before with { GameplayOwnership = Guid.NewGuid() } } } }] }));
        var current = saved.Current.Single();
        RejectCommonReceipt(call with { Before = call.Before with { Stack = new string('9', 64) } });
        RejectCommonReceipt(call with { Before = call.Before with { ExecutableSha256 = new string('8', 64) } });
        RejectCommonReceipt(call with { Trace = call.Trace with { DeltaBits = Bits(.5f) } });
        RejectCommonReceipt(call with { AfterFields = call.AfterFields with { Stores = call.AfterFields.Stores + 1 } });
        using var cold = new FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration.ForExecutable(source.Controls.Main.EngineSha256),
            stack, Identity, _ => throw new InvalidOperationException("Cold replayed gameplay."), _ => null, saved);
        Require(cold.ReadOpacity(actor, 1).Teleport == call.AfterFields && cold.ReadOpacity(actor, 1).Opacity.Stores == 1,
            "Cold High constructor replayed fade or effective alpha.");
        var missing = new FalloutPlayerOpacityState(source, stack, Guid.NewGuid(), () => first, () => null, () => Bits(.1f), _ => Bits(1), (_, _) => { });
        var failedInvocation = Guid.NewGuid();
        Reject(() => cold.UpdateTeleportFade(source, actor, 1, failedInvocation, (_, read, store) => missing.BindTeleportConsumers(main, 1, failedInvocation, read, store)));
        var failed = cold.Capture().Current.Single().SceneFields!;
        Require(failed.Opacity is { Stores: 2 } && failed.TeleportCall!.Trace is
            { FailedAt: FalloutActorTeleportFadeStep.Opacity, Result: null } && missing.Capture().Call is
            { EffectiveStored: true, FailedAt: FalloutPlayerOpacityStep.FirstPerson },
            "A missing nonnull first-person model erased current High alpha or manufactured a returned child.");
        FalloutActorProcessCommonState.Validate(RoundTrip(cold.Capture()));
        var wrong = new FadeConsumers { Invocation = Guid.NewGuid(), Timer = Bits(1), Seconds = Bits(1) };
        var untouched = FalloutActorTeleportFadeFields.Constructed;
        Reject(() => FalloutActorTeleportFade.Execute(Guid.NewGuid(), true, wrong, () => untouched, value => untouched = value, _ => { }));
        Require(untouched == FalloutActorTeleportFadeFields.Constructed, "Foreign caller nonce crossed a source fade store.");
        opacity.Retire(); missing.Retire(); scene.Retire();
        void RejectCommonReceipt(FalloutProcessTeleportFadeUpdate malformed) => Reject(() =>
            FalloutActorProcessCommonState.Validate(saved with
            { Current = [current with { SceneFields = current.SceneFields! with { TeleportCall = malformed } }] }));
    }
    private static FalloutPlayerOpacitySource Source()
    {
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        return FalloutPlayerOpacitySource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
    }
    private sealed class FadeConsumers : IFalloutActorTeleportFadeConsumers
    {
        public string Owner => "authored-teleport-child";
        public Guid Invocation { get; set; }
        internal uint Timer, Seconds;
        internal int TimerReads, OpacityCalls;
        public uint ReadMainScaledTimerBits() { TimerReads++; return Timer; }
        public uint ReadTeleportSecondsBits(string setting) { Require(setting == FalloutPlayerOpacitySource.PlayerFadeSeconds, "Fade used the Actor setting for Player."); return Seconds; }
        public void ApplyOpacity() { OpacityCalls++; }
        public void MoveToTarget(FalloutFormKey target) => throw new NotSupportedException("authored-target-child-refused");
        public bool ReadNpcTeleportCompletion() => throw new NotSupportedException("authored-NPC-child-refused");
        public void DisableActor() => throw new NotSupportedException("authored-disable-child-refused");
        public void EnableActor() => throw new NotSupportedException("authored-enable-child-refused");
    }
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or ObjectDisposedException) { return; }
        throw new InvalidOperationException("Unsupported source fade/opacity state was accepted.");
    }
}
