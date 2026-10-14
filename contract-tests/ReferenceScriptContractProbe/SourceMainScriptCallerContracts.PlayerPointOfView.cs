using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerPointOfViewContracts(FalloutMainScriptCallerSource main, string stack)
    {
        RunPlayerOwnerLineageContracts(main, stack);
        var command = new FalloutPlayerControlCommand(false, [true, true, true, true, true]);
        Require(FalloutPlayerControlFieldsState.CommandMask(command) == 0x1f,
            "Five explicit source literal-one arguments were replaced by the omitted command defaults.");
        FalloutStandalonePlayerSceneSnapshot saved;
        using (var fixture = new PointOfViewFixture(main, stack))
        {
            fixture.Apply(command);
            saved = fixture.Scene.Capture();
            var initialView = saved.PointOfView ?? throw new InvalidDataException("Actual POV command omitted its retained field owner.");
            var initialControls = saved.ControlFields ?? throw new InvalidDataException("Actual POV command omitted its flag fields.");
            var initialCall = initialControls.Call ?? throw new InvalidDataException("Actual POV command omitted its flag writer.");
            var initialEffects = initialCall.Effects ?? throw new InvalidDataException("Actual POV command omitted its effect prefix.");
            Require(saved is { PlayerView: 0, FirstPerson: null, ControlFields: { DisabledWord: 0x1f, FlagStores: 2, Call.Returned: true },
                PointOfView: { DesiredView: 0, SharedScalarBits: 0, DesiredViewStores: 1, SharedScalarStores: 1, Calls.Count: 1 } } &&
                initialView.Calls[0] is { GetterReturned: true, FirstPerson: null, DesiredViewStored: not null,
                    SharedScalarStored: not null, Returned: true, Failure: null } &&
                initialEffects.PointOfView == initialView.Calls[0],
                "Constructor-null POV bypassed its two real stores, aliased model view or lost the exact source command.");
            Reject(() => fixture.Scene.DisableControlPointOfView(Guid.NewGuid()));
            var beforeForeign = JsonSerializer.Serialize(saved.PointOfView);
            Exception? foreign = null;
            var worker = new Thread(() => { try { fixture.Scene.DisableControlPointOfView(Guid.NewGuid()); } catch (Exception error) { foreign = error; } });
            worker.Start(); worker.Join();
            Require(foreign is InvalidOperationException && JsonSerializer.Serialize(fixture.Scene.Capture().PointOfView) == beforeForeign,
                "POV admitted a foreign thread or stores outside the actual flag writer.");
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { PointOfView = initialView with { SharedScalarStores = 0 } }));
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { PointOfView = initialView with { DesiredView = 1 } }));
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { PointOfView = initialView with { SharedScalarBits = 0x80000000 } }));
            fixture.CompleteTransitions(); saved = fixture.Scene.Capture();
        }
        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var cold = new PointOfViewFixture(main, stack, JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!);
            var before = cold.Scene.Capture();
            var restoredView = before.PointOfView ?? throw new InvalidDataException("Cold POV omitted its current field owner.");
            var previousView = saved.PointOfView ?? throw new InvalidDataException("Cold POV omitted its previous field owner.");
            var restoredHud = before.ControlHud ?? throw new InvalidDataException("Cold POV omitted its actual HUD owner.");
            var previousHud = saved.ControlHud ?? throw new InvalidDataException("Cold POV omitted its previous HUD owner.");
            Require(before.CapturedProcess != saved.CapturedProcess && before.Constructor != saved.Constructor &&
                restoredView.DesiredViewStores == previousView.DesiredViewStores &&
                restoredView.SharedScalarStores == previousView.SharedScalarStores &&
                JsonSerializer.Serialize(restoredView.Calls) == JsonSerializer.Serialize(previousView.Calls),
                "Cold POV replayed its stores, source command or camera callback instead of retaining original receipts.");
            Require(restoredHud.Lifetimes is { } lifetimes && lifetimes.Count == cycle + 2 &&
                JsonSerializer.Serialize(restoredHud.Call) == JsonSerializer.Serialize(previousHud.Call),
                "Repeated cold HUD promoted a retained intermediate call into its current factory or replayed its writes.");
            Reject(() => FalloutPlayerControlHudState.Validate(restoredHud with { Lifetimes = [new(restoredHud.Process, restoredHud.Factory)] },
                FalloutPlayerControlHudSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main)), stack));
            cold.Apply(command); cold.CompleteTransitions(); saved = cold.Scene.Capture();
            var nextView = saved.PointOfView ?? throw new InvalidDataException("Post-cold command omitted its actual POV owner.");
            Require(nextView.DesiredViewStores == cycle + 2 && nextView.SharedScalarStores == cycle + 2 &&
                nextView.Lifetimes.Count == cycle + 2 && nextView.Calls[^1].Process == saved.CapturedProcess,
                "Fresh post-cold command reused a prior process or omitted its actual constructor/store lifetime.");
        }
        using (var nonnull = new PointOfViewFixture(main, stack))
        {
            var scene = nonnull.Scene;
            var first = scene.PublishFirstPerson(new(Guid.NewGuid(), scene.Process, scene.Source.Identity, scene.Stack,
                42, "meshes/authored-current-first-person.nif", new string('3', 64), "authored-actual-first-person-publication"), () => true);
            Reject(() => nonnull.Apply(command)); var failed = scene.Capture();
            var failedView = failed.PointOfView ?? throw new InvalidDataException("Nonnull POV omitted its failed field prefix.");
            Require(failed is { Failure: FalloutStandalonePlayerSceneState.PointOfViewCameraBoundary,
                PointOfView: { DesiredViewStores: 1, SharedScalarStores: 0, Calls.Count: 1 } } &&
                failedView.Calls[0] is { FirstPerson: not null, DesiredViewStored: not null,
                    SharedScalarStored: null, Step: FalloutPlayerPointOfViewStep.CameraChild, Returned: false },
                "Nonnull POV erased its byte store or invented a returned live camera child/shared scalar write.");
            var cold = new FalloutStandalonePlayerSceneState(scene.Source, stack, Guid.NewGuid(),
                JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(failed))!);
            var retainedFailure = cold.Capture().PointOfView ?? throw new InvalidDataException("Cold POV erased its failed field prefix.");
            Require(JsonSerializer.Serialize(retainedFailure.Calls) == JsonSerializer.Serialize(failedView.Calls) &&
                retainedFailure.SharedScalarStores == 0 && cold.SaveBlocker is not null,
                "Cold failed POV replayed source callbacks or promoted its old live model to a current null field.");
            scene.ReleaseFirstPerson(first);
        }
        Console.WriteLine("OPENNV_PLAYER_POV_PREFIX_PASS fiveLiteralArguments=true mask1F=true currentFirstPersonField=true " +
            "twoNullArmStores=true nonnullByteStoreBeforeRefusal=true foreignThreadRejected=true threeColdComponentCyclesNoReplay=true gameplay=UNEXECUTED");
    }

    private sealed class PointOfViewFixture : IDisposable
    {
        internal FalloutStandalonePlayerSceneState Scene { get; }
        private readonly FalloutPlayerMovementControlSource _controls;
        private readonly FalloutDirectInputState _input;
        private readonly FalloutPlayerControlHudState _hud;
        private readonly FalloutPlayerControlHudContext _context;
        private readonly FalloutStandaloneInterfaceState _interface;
        private readonly FalloutSourceFrameTimerState _timer;
        private readonly FalloutRendererClockState _clock;
        private readonly FalloutActorProcessCommonState _common;
        private readonly FalloutPlayerControlFightingOwner _fighting;
        private readonly ControlHudCounter _counter = new();

        internal PointOfViewFixture(FalloutMainScriptCallerSource main, string stack, FalloutStandalonePlayerSceneSnapshot? saved = null)
        {
            _controls = FalloutPlayerMovementControlSource.Reviewed(main);
            Scene = new(FalloutStandalonePlayerSceneSource.Read(main), stack, Guid.NewGuid(), saved);
            _input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null)); Scene.PublishControlCommandInput(_input);
            var source = FalloutPlayerControlHudSource.Reviewed(_controls); var settings = AuthoredControlHudSettings(source, stack);
            var timerSource = FalloutSourceFrameTimer.Read(new(main.EngineSha256, new('a', 64), new('b', 64), new('c', 64),
                new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
                new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
            _timer = new(timerSource, _counter, Scene.Process);
            _clock = new(new(FalloutRendererClockSource.Reviewed(main), "authored-POV-INI",
                new(new("iUpdateType:HAVOK", FalloutIniCollection.Main, 0), 0, "authored-update"),
                new(new("fChaseDeltaMult:HAVOK", FalloutIniCollection.Main, 0x3d4ccccd), .05f, "authored-chase")), Scene.Process);
            _interface = new(FalloutStandaloneInterfaceSource.Read(main), stack, Scene.Process);
            var display = new FalloutPlayerControlDisplayState(source, stack, Scene.Process, saved?.ControlDisplay);
            if (saved is null)
            {
                display.ApplyDeclaredConfiguration(settings);
                display.PublishCurrentLayout(Guid.NewGuid(), "authored-POV-source-layout", () => Scene.ControlFields.RequireActualInput(_input));
            }
            _context = new(source, stack, Scene.Process, _interface, _timer, _clock, _input, Scene.ControlFields);
            _hud = new(source, stack, Scene.Process, AuthoredControlHudComponents(), settings, Scene.ThreadContext, _context, saved?.ControlHud);
            Scene.BindControlHud(_hud, display);
            if (saved is null) _hud.Initialize(display, Scene.ControlFields.ReadDisabledWord);
            var player = new FalloutFormKey("PointOfViewFixture.esm", 0x14);
            _common = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256), stack,
                actor => new(actor, new("PointOfViewFixture.esm", 7), "ENGINE_PLAYER", 0, main.EngineSha256, "NPC_", 0, new('4', 64), true),
                _ => throw new InvalidOperationException("POV queried unrelated gameplay pools."),
                _ => throw new InvalidOperationException("POV queried a native body."));
            _common.Construct(player, 1, FalloutDetectionProcessLevel.High);
            _fighting = new(_controls, stack, Scene.Process, _common, player, 1, () => 0,
                () => throw new InvalidOperationException("Constructor mover flags entered a control-mode query."), () => { });
        }
        internal void Apply(FalloutPlayerControlCommand command)
        {
            var observation = new FalloutPlayerControlCommandObservation(new("PointOfViewFixture.esm", 0x30),
                "authored-actual-POV-command", Guid.NewGuid(), command.Enable, command.Arguments);
            Scene.ApplySourceControlCommand(observation, new FalloutPlayerControlFlagConsumers(_controls, Scene.Stack, Scene.Process,
                observation.Observation, Scene.ControlFields, _hud, _fighting, Scene));
        }
        internal void CompleteTransitions()
        { _counter.Now += 50; _timer.Update(); _interface.AdvanceHudMenuTransitions(_context.ReadTransitionFrame()); }
        public void Dispose()
        {
            _hud.Retire(); Scene.ThreadContext.Retire(); _interface.Retire(); _input.Retire(); _timer.Dispose(); _clock.Dispose(); _common.Dispose();
        }
    }
}
