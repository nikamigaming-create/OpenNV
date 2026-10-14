using System.Text.Json;
using System.Xml.Linq;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerControlHudContracts()
    {
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var controls = FalloutPlayerMovementControlSource.Reviewed(main);
        var source = FalloutPlayerControlHudSource.Reviewed(controls);
        var stack = new string('2', 64); var process = Guid.NewGuid();
        var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null));
        var fields = new FalloutPlayerControlFieldsState(controls, stack, process);
        fields.BindActualInput(input);
        var thread = new FalloutPlayerThreadContextState(FalloutPlayerThreadContextSource.Reviewed(main), process);
        var ui = AuthoredControlHudComponents(); var settings = AuthoredControlHudSettings(source, stack);
        var display = new FalloutPlayerControlDisplayState(source, stack, process);
        Require(display.Read() is { Width: 640, Height: 480, Wide: 0, LogicalWidthBits: 0x44a00000, LogicalHeightBits: 0x44700000 },
            "HUD constructor dimensions were replaced by presentation pixels.");
        display.ApplyDeclaredConfiguration(settings);
        display.PublishCurrentLayout(Guid.NewGuid(), "authored-actual-source-layout", () => { });
        var projected = display.Read();
        Require(projected.Width == 1920 && projected.Height == 1080 && projected.Wide == 1 &&
            projected.LogicalWidthBits == BitConverter.SingleToUInt32Bits((float)((double)1920 / 1080 * 960)),
            "Current source dimensions lost their independent layout byte or normalized getter precision.");
        var layout = display.Capture();
        Reject(() => FalloutPlayerControlDisplayState.Validate(layout with { Wide = 0 }, source, stack));
        var rejectedPublication = Guid.NewGuid();
        Reject(() => display.PublishSelectedMode(rejectedPublication, "authored-actual-mode", 800, 600, 1, () => { }));
        display.PublishSelectedMode(rejectedPublication, "authored-actual-mode", 800, 600, 0, () => { });
        var counter = new ControlHudCounter();
        var timerSource = FalloutSourceFrameTimer.Read(new(main.EngineSha256,
            new('a', 64), new('b', 64), new('c', 64),
            new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
            new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
        using var timer = new FalloutSourceFrameTimerState(timerSource, counter, process);
        using var clock = new FalloutRendererClockState(new(FalloutRendererClockSource.Reviewed(main), "authored-declared-INI",
            new(new("iUpdateType:HAVOK", FalloutIniCollection.Main, 0), 0, "authored-update-type"),
            new(new("fChaseDeltaMult:HAVOK", FalloutIniCollection.Main, 0x3d4ccccd), .05f, "authored-chase")), process);
        var actualInterface = new FalloutStandaloneInterfaceState(FalloutStandaloneInterfaceSource.Read(main), stack, process);
        var context = new FalloutPlayerControlHudContext(source, stack, process, actualInterface, timer, clock, input, fields);
        var hud = new FalloutPlayerControlHudState(source, stack, process, ui, settings, thread, context);
        Reject(() => hud.RequireLocalTimerConstructor());
        hud.Initialize(display, fields.ReadDisabledWord);
        var initialized = hud.Capture();
        Require(initialized is { InitializationReturned: true, Mode: 5, MenuMode: 8, VisibilityMask: 0, AllowedMask: 0xffff,
            Call: { Returned: true, Allocation: { BeforeKind: 59, EnteredKind: 13, RestoredKind: 59, Returned: true },
                Frame.Clock.HasReturnedUpdate: false, Frame.Clock.ScaledSecondsBits: 0, Frame.ControlMode: 0 } },
            "Real HUD initialization skipped source TLS, queried a invented frame delta, or used the native render clock.");
        Require(actualInterface.Capture().Menus.Transitions.Count == 1 && initialized.Transitions.Count == 1,
            "HUD initialized a private transition queue instead of the actual Interface queue.");
        var player = new FalloutFormKey("ControlHudFixture.esm", 0x14); var drift = false;
        FalloutCombatActorIdentity Identity(FalloutFormKey actor) => new(actor, new("ControlHudFixture.esm", drift ? 8u : 7u),
            "ENGINE_PLAYER", 0, main.EngineSha256, "NPC_", 0, new('4', 64), true);
        using var common = new FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256), stack,
            Identity, _ => throw new InvalidOperationException("HUD fighting getter queried gameplay pools."),
            _ => throw new InvalidOperationException("HUD fighting getter queried a native body."));
        common.Construct(player, 1, FalloutDetectionProcessLevel.High);
        var commonBefore = common.Capture(); var modeReads = 0;
        var fighting = new FalloutPlayerControlFightingOwner(controls, stack, process, common, player, 1,
            () => 0, () => { ++modeReads; return 0; }, () => { });
        var invocation = Guid.NewGuid();
        var effects = new FalloutPlayerControlFlagConsumers(controls, stack, process, invocation, fields, hud, fighting);
        fields.ApplyCommand(invocation, "authored-actual-bootstrap-command", new(false, []), effects);
        var returned = fields.Capture(); var currentHud = hud.Capture();
        Require(returned is { DisabledWord: 0x0d, FlagStores: 2, Call: { Returned: true, InputType: 12, Effects.Fighting:
                { Stores: 0, Drawn: false, ControlMode: null, Returned: true } } } &&
            currentHud is { Mode: 12, VisibilityMask: 0x1514, Call: { Requested: 12, Returned: true } } &&
            modeReads == 0 && common.Capture().Sequence == commonBefore.Sequence,
            "Bootstrap control command bypassed mandatory HUD or changed constructor-false process state.");
        for (var index = 0; index < FalloutPlayerControlHudSource.VisibilityTiles.Count; ++index)
            Require(BitConverter.SingleToUInt32Bits(ui.GetFloat("HUDMainMenu/" + FalloutPlayerControlHudSource.VisibilityTiles[index] + "/visible")) ==
                BitConverter.SingleToUInt32Bits(0x1514u & (1u << index)), "HUD visible trait replaced a numeric source bit with Boolean one.");
        Reject(() => FalloutPlayerControlFieldsState.Validate(returned with { Call = returned.Call! with
        { Effects = returned.Call.Effects! with { Observation = Guid.NewGuid() } } }, controls, stack));
        Reject(() => FalloutPlayerControlHudState.Validate(currentHud with { Display = currentHud.Display! with { LogicalWidthBits = 0 } }, source, stack));
        counter.Now = 50; timer.Update();
        actualInterface.AdvanceHudMenuTransitions(context.ReadTransitionFrame());
        Require(actualInterface.Capture().Menus.Transitions.Count == 0 && hud.Capture().Transitions.Count == 0,
            "The actual source timer/queue failed to remove its completed menu transition.");
        RunControlHudColdValuesContract(source, stack, settings, display.Capture(), returned, hud.Capture(), timerSource);
        Exception? foreignThread = null;
        var worker = new Thread(() => { try { fighting.DisableFighting(); } catch (Exception error) { foreignThread = error; } });
        worker.Start(); worker.Join(); Require(foreignThread is InvalidOperationException, "Fighting getter accepted a foreign source thread.");
        drift = true; Reject(fighting.RequireCurrent); drift = false;
        Reject(() => new FalloutPlayerControlFightingOwner(controls, stack, process, common, player, 2, () => 0, () => 0, () => { }));
        var originalHistory = common.Capture(); var observed = Guid.NewGuid();
        common.ObserveWeaponDrawnWriter(player, 1, observed, "authored-observed-current-process-writer");
        Reject(() => fighting.DisableFighting());
        var bypass = new FalloutPlayerControlFightingOwner(controls, stack, process, common, player, 1,
            () => 0x800, () => { ++modeReads; return 0; }, () => { });
        Require(bypass.DisableFighting() is { Drawn: false, Stores: 0, ControlMode: 0 } && modeReads == 1,
            "Mover800 source arm incorrectly queried the skipped process byte.");
        common.ObserveWeaponDrawnWriter(player, 1, Guid.NewGuid(), "authored-second-current-process-writer");
        Require(originalHistory.Current.Single().SceneFields!.WeaponDrawn!.Observations.Count == 0,
            "Published process snapshot mutated when later observations appended.");
        Reject(() => common.ObserveWeaponDrawnWriter(player, 1, observed, "authored-current-process-writer"));
        Require(common.SaveBlocker is not null, "Unavailable weapon-drawn fields admitted a complete save.");
        var next = Guid.NewGuid();
        Reject(() => fields.ApplyCommand(next, "authored-actual-POV-command", new(false, [false, false, false, true, false, false, false]),
            new FalloutPlayerControlFlagConsumers(controls, stack, process, next, fields, hud, bypass)));
        var failed = fields.Capture();
        Require(failed is { DisabledWord: 0x1d, Call: { Step: FalloutPlayerControlFlagStep.PointOfView, Effects.Hud.Call.Returned: true,
            Returned: false, Failure: "source-Player-POV-current-child-and-shared-camera-writers-unbound" } },
            "Failed POV callback erased the prior full-byte, HUD or fighting prefix.");
        var uiBeforeCold = ui.Revision;
        var cold = new FalloutPlayerControlFieldsState(controls, stack, Guid.NewGuid(),
            JsonSerializer.Deserialize<FalloutPlayerControlFieldsSnapshot>(JsonSerializer.Serialize(failed))!);
        Require(JsonSerializer.Serialize(cold.Capture().Call) == JsonSerializer.Serialize(failed.Call) && cold.Capture().FlagStores == 4 && ui.Revision == uiBeforeCold,
            "Cold failed controls replayed HUD/process effects or lost the original source call identity.");
        Reject(() => cold.ReadDisabledWord());
        RunHudOverrideFaultContracts(main, stack);
        hud.Retire(); thread.Retire(); actualInterface.Retire(); input.Retire();
        RunPlayerPointOfViewContracts(main, stack);
        RunExperienceSourceHudContracts(main, stack);
        Console.WriteLine("OPENNV_PLAYER_CONTROL_HUD_PASS authored=true actualSourceXml=true sharedInterfaceQueue=true " +
            "sourceConstructorDelta=true kind13PriorRestored=true disable0D=true processFalseZeroStores=true " +
            "noNativeBodyQuery=true coldTileValuesNoEffectReplay=true failedPrefixColdNoReplay=true completeGameplay=UNEXECUTED");
    }

    private static void RunControlHudColdValuesContract(FalloutPlayerControlHudSource source, string stack,
        FalloutNumericIniSettings settings, FalloutPlayerControlDisplaySnapshot displaySaved,
        FalloutPlayerControlFieldsSnapshot fieldsSaved, FalloutPlayerControlHudSnapshot hudSaved, FalloutSourceFrameTimer timerSource)
    {
        var process = Guid.NewGuid();
        var fields = new FalloutPlayerControlFieldsState(source.Controls, stack, process,
            JsonSerializer.Deserialize<FalloutPlayerControlFieldsSnapshot>(JsonSerializer.Serialize(fieldsSaved))!);
        var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null)); fields.BindActualInput(input);
        var counter = new ControlHudCounter(); using var timer = new FalloutSourceFrameTimerState(timerSource, counter, process);
        using var clock = new FalloutRendererClockState(new(FalloutRendererClockSource.Reviewed(source.Controls.Main), "authored-cold-INI",
            new(new("iUpdateType:HAVOK", FalloutIniCollection.Main, 0), 0, "authored-cold-update"),
            new(new("fChaseDeltaMult:HAVOK", FalloutIniCollection.Main, 0x3d4ccccd), .05f, "authored-cold-chase")), process);
        var currentInterface = new FalloutStandaloneInterfaceState(FalloutStandaloneInterfaceSource.Read(source.Controls.Main), stack, process);
        var thread = new FalloutPlayerThreadContextState(FalloutPlayerThreadContextSource.Reviewed(source.Controls.Main), process);
        var display = new FalloutPlayerControlDisplayState(source, stack, process,
            JsonSerializer.Deserialize<FalloutPlayerControlDisplaySnapshot>(JsonSerializer.Serialize(displaySaved))!);
        var ui = AuthoredControlHudComponents();
        var context = new FalloutPlayerControlHudContext(source, stack, process, currentInterface, timer, clock, input, fields);
        var reads = counter.Reads;
        var hud = new FalloutPlayerControlHudState(source, stack, process, ui, settings, thread, context,
            JsonSerializer.Deserialize<FalloutPlayerControlHudSnapshot>(JsonSerializer.Serialize(hudSaved))!);
        var restored = hud.Capture();
        Require(restored.Process != hudSaved.Process && restored.Factory != hudSaved.Factory && restored.UiFactory != hudSaved.UiFactory &&
            restored.Sequence == hudSaved.Sequence && restored.InitializationReturned && hud.SaveBlocker is null &&
            JsonSerializer.Serialize(restored.Call) == JsonSerializer.Serialize(hudSaved.Call) &&
            JsonSerializer.Serialize(restored.Initialization) == JsonSerializer.Serialize(hudSaved.Initialization) &&
            JsonSerializer.Serialize(restored.Tiles) == JsonSerializer.Serialize(hudSaved.Tiles) && counter.Reads == reads &&
            currentInterface.Capture().Menus.Transitions.Count == 0 && thread.CaptureKindScopes().Count == 0 && fields.Capture().FlagStores == fieldsSaved.FlagStores,
            "Cold HUD replayed initialization/clock/TLS/effects or lost its retained actual tile and call fields.");
        Reject(() => hud.Initialize(display, fields.ReadDisabledWord));
        hud.Update(Guid.NewGuid(), 12, fields.ReadDisabledWord);
        Require(hud.Capture().Call is { Returned: true } updated && updated.Process == process && updated.HudFactory == hud.Factory &&
            updated.Allocation is { BeforeKind: 59, RestoredKind: 59, Returned: true } && counter.Reads == reads,
            "A fresh HUD call after cold restore reused its historical owner or queried a new raw clock.");
        var tile = ui.BindComponent("HUDMainMenu/ActionPoints"); var revision = ui.Revision;
        ui.StoreFloat(tile, "rawZero", 0); var positiveRevision = ui.Revision;
        ui.StoreFloat(tile, "rawZero", BitConverter.UInt32BitsToSingle(0x80000000));
        Require(positiveRevision > revision && ui.Revision > positiveRevision &&
            BitConverter.SingleToUInt32Bits(ui.ReadFloat(tile, "rawZero")) == 0x80000000,
            "Exact source Float32 store lost a changed signed-zero bit or presentation revision.");
        Require(ui.SetFloatGradual("HUDMainMenu/ActionPoints/visible", 0, 1, .5), "Authored gradual source writer failed to bind.");
        Require(hud.SaveBlocker is not null && hud.Capture().Tiles!.Any(value => value.Animated),
            "Live animated source tile admitted an unsupported cold reconstruction.");
        ui.Reset(); Require(hud.SaveBlocker is not null && hud.Capture().Tiles!.All(value => !value.Alive),
            "Reset source XML epoch preserved obsolete actual component bindings.");
        hud.Retire(); thread.Retire(); currentInterface.Retire(); input.Retire();
    }

    private static void RunHudOverrideFaultContracts(FalloutMainScriptCallerSource main, string stack)
    {
        var owner = new FalloutStandaloneInterfaceState(FalloutStandaloneInterfaceSource.Read(main), stack, Guid.NewGuid());
        var called = 0;
        owner.ApplyHudMovementOverride(Guid.NewGuid(), 256, value => { ++called; Require(value == 0, "Override did not narrow its original argument."); });
        owner.ApplyHudMovementOverride(Guid.NewGuid(), 0, _ => ++called);
        Require(called == 2 && owner.Capture().HudControl is { MovementOverride: 0, Stores: 2, Call.Returned: true },
            "Equal or zero override skipped its mandatory Player mask child.");
        Reject(() => owner.ApplyHudMovementOverride(Guid.NewGuid(), 1, _ =>
            Reject(() => owner.ApplyHudMovementOverride(Guid.NewGuid(), 0, _ => { }))));
        Require(owner.Capture().HudControl is { MovementOverride: 1, Stores: 3, Call: { Returned: false, Failure: not null } } &&
            owner.SaveBlocker is not null, "Swallowed override reentry claimed a complete source callback.");
        Reject(() => owner.ReadHudMovementOverride()); owner.Retire();
    }

    private static FalloutNumericIniSettings AuthoredControlHudSettings(FalloutPlayerControlHudSource source, string stack) => new(
        [new(FalloutPlayerControlHudSource.CrosshairSetting, FalloutIniCollection.Prefs, 1),
            new(FalloutPlayerControlHudSource.CrosshairSetting, FalloutIniCollection.Main, 0),
            new(FalloutPlayerControlHudSource.SafeXSetting, FalloutIniCollection.Main, 15), new(FalloutPlayerControlHudSource.SafeYSetting, FalloutIniCollection.Main, 15),
            new(FalloutPlayerControlHudSource.WideXSetting, FalloutIniCollection.Main, 15), new(FalloutPlayerControlHudSource.WideYSetting, FalloutIniCollection.Main, 15),
            new(FalloutPlayerControlHudSource.WidthSetting, FalloutIniCollection.Renderer, 640), new(FalloutPlayerControlHudSource.HeightSetting, FalloutIniCollection.Renderer, 480)],
        [new("authored-separate-Main-crosshair", FalloutIniCollection.Main, [new("GamePlay", "bCrossHair", "0")]),
            new("authored-effective-Prefs-crosshair", FalloutIniCollection.Prefs, [new("GamePlay", "bCrossHair", "-1")]),
            new("authored-effective-renderer-display", FalloutIniCollection.Renderer,
            [new("Display", "iSize W", "1920"), new("Display", "iSize H", "1080")])], stack + ":authored-owned-executable:" + source.Controls.Main.EngineSha256);

    private static FalloutUiComponentStore AuthoredControlHudComponents() => FalloutUiComponentStore.Synthetic(
        new XElement("menu", new XAttribute("name", "HUDMainMenu"), new XElement("fadein", .01f), new XElement("fadeout", .02f),
            FalloutPlayerControlHudSource.VisibilityTiles.Select(name => new XElement("rect", new XAttribute("name", name),
                new XElement("visible", 1), name == "XPMeter" ? new XElement("user7", 99) : null)),
            Template("template_justify_center_text", "text", "justify_center_text")));

    private sealed class ControlHudCounter : IFalloutSourceTickCounter
    {
        public string Owner => "authored-actual-control-clock";
        internal uint Now;
        internal int Reads;
        public uint Read() { ++Reads; return Now; }
        public void Wait(uint milliseconds) { }
    }
}
