using System.Text.Json;
using System.Xml.Linq;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunExperienceSourceHudContracts(FalloutMainScriptCallerSource main, string stack)
    {
        using (var empty = new ExperienceSourceHudFixture(main, stack))
            ExperienceNotificationContracts.VerifyEmptySourceFrameRefusal(empty.Source);
        using var warm = new ExperienceSourceHudFixture(main, stack);
        var initial = warm.Source.Capture();
        Require(initial.InitialQueueItems == 0 && initial.InitialSuppression == 0 && initial.ConstructorWallTick == 42 &&
            initial.Tiles.Count == 9 && warm.Interface.Capture().Menus.Transitions.Count == 1 && warm.Counter.Reads == 2 &&
            initial.LocalTimerConstructor == warm.Hud.Capture().LocalTimerConstructor,
            "XP source constructor skipped its genuine retained children or consumed the original Interface transition.");
        var bracket = warm.Ui.BindComponent("HUDMainMenu/XPMeter/XPBracket");
        warm.Ui.StoreFloat(bracket, "rawZero", BitConverter.UInt32BitsToSingle(0x80000000));
        warm.Ui.StoreString(warm.Ui.BindComponent("HUDMainMenu/XPMeter/XPAmount"), "string", "authored original source text");
        warm.CompleteTransitions();
        var retained = JsonSerializer.Deserialize<FalloutExperienceSourceHudSnapshot>(JsonSerializer.Serialize(warm.Source.Capture())) ??
            throw new InvalidDataException("XP source constructor omitted its cold state.");
        using var cold = new ExperienceSourceHudFixture(main, stack, warm.Hud.Capture(), warm.Display.Capture(), retained);
        var restored = cold.Source.Capture();
        FalloutExperienceSourceHud.ValidateAssociation(retained, warm.Hud.Capture(), warm.Display.Capture(), warm.Source.Process, stack);
        FalloutExperienceSourceHud.ValidateAssociation(restored, cold.Hud.Capture(), cold.Display.Capture(), cold.Source.Process, stack);
        Require(restored.Process != retained.Process && restored.Factory != retained.Factory && restored.UiFactory != retained.UiFactory &&
            restored.HudFactory != retained.HudFactory && restored.Initialization == retained.Initialization &&
            restored.ConstructorWallTick == retained.ConstructorWallTick && restored.LocalTimerConstructor == retained.LocalTimerConstructor &&
            JsonSerializer.Serialize(restored.Tiles) == JsonSerializer.Serialize(retained.Tiles) &&
            cold.Interface.Capture().Menus.Transitions.Count == 0 && cold.Counter.Reads == 1,
            "Cold XP source ownership reused old factories, replayed the HUD initialization or changed original source tile values.");
        ExperienceNotificationContracts.VerifySourceHudRetiredHistory(warm.Source, cold.Source);
        ExperienceNotificationContracts.VerifySourceHudPublication(warm.Source);
        var entered = warm.Source.Capture();
        var refusal = entered.FrameRefusal ?? throw new InvalidDataException("Reached XP source frame omitted its failed prefix.");
        Require(refusal.Process == warm.Source.Process && refusal.OwnerFactory == warm.Source.Factory &&
            refusal.DeliveredClock == new FalloutExperienceClockReading(10, 10) && refusal.Admission == FalloutExperienceNotificationAdmission.Ready &&
            !refusal.LevelRequestPending && refusal.CharacterGenerationEnded &&
            JsonSerializer.Serialize(entered.Tiles) == JsonSerializer.Serialize(retained.Tiles) &&
            entered.LocalTimerConstructor == retained.LocalTimerConstructor,
            "Actual unowned source frame changed its original args, tile writes or timer state.");
        var failedSaved = JsonSerializer.Deserialize<FalloutExperienceSourceHudSnapshot>(JsonSerializer.Serialize(entered)) ??
            throw new InvalidDataException("Entered source frame omitted its actual cold prefix.");
        var failedCold = new ExperienceSourceHudFixture(main, stack, warm.Hud.Capture(), warm.Display.Capture(), failedSaved);
        using (failedCold)
        {
            var history = failedCold.Source.Capture();
            Require(history.FrameRefusal == refusal && failedCold.Source.SaveBlocker == FalloutExperienceSourceHud.FrameBoundary &&
                failedCold.Counter.Reads == 1 && history.LocalTimerConstructor == entered.LocalTimerConstructor,
                "Cold source HUD replayed or cleared its original reached frame failure.");
            Reject(() => failedCold.Source.EnterCurrentFrame(new(20, 20), FalloutExperienceNotificationAdmission.Held, true, false));
            Require(failedCold.Source.Capture().FrameRefusal == refusal, "Cold frame retry replaced its original failed source prefix.");
        }
        Exception? foreign = null;
        var worker = new Thread(() => { try { warm.Source.Capture(); } catch (Exception error) { foreign = error; } });
        worker.Start(); worker.Join();
        Require(foreign is InvalidOperationException, "XP source constructor borrowed a foreign thread.");
        Reject(() => FalloutExperienceSourceHud.Validate(retained with { InitialQueueItems = 1 }));
        Reject(() => FalloutExperienceSourceHud.Validate(retained with { LocalTimerConstructor = null }));
        Reject(() => FalloutExperienceSourceHud.Validate(retained with { Tiles = retained.Tiles.Take(8).ToArray() }));
        Reject(() => FalloutExperienceSourceHud.ValidateAssociation(retained with { HudFactory = Guid.NewGuid() },
            warm.Hud.Capture(), warm.Display.Capture(), warm.Source.Process, stack));
        Reject(() => FalloutExperienceSourceHud.ValidateAssociation(retained with { Display = retained.Display with { Factory = Guid.NewGuid() } },
            warm.Hud.Capture(), warm.Display.Capture(), warm.Source.Process, stack));
        Reject(() => FalloutExperienceSourceHud.ValidateAssociation(retained with { UiEpoch = retained.UiEpoch + 1 },
            warm.Hud.Capture(), warm.Display.Capture(), warm.Source.Process, stack));
        var root = retained.Tiles[0];
        var substituted = retained.Tiles.ToArray();
        substituted[0] = root with { Floats = root.Floats.Append(new FalloutUiFloatValue("foreign-source-value", 0)).ToArray() };
        Reject(() => FalloutExperienceSourceHud.ValidateAssociation(retained with { Tiles = substituted },
            warm.Hud.Capture(), warm.Display.Capture(), warm.Source.Process, stack));
        Require(warm.Ui.SetFloatGradual("HUDMainMenu/XPMeter/XPBracket/alpha", 0, 1, 1), "XP authored gradual writer did not bind.");
        Require(warm.Source.SaveBlocker is not null, "XP source constructor admitted an unowned active gradual writer.");
        Reject(() => warm.Source.Capture());
        warm.Ui.Reset(); Reject(() => warm.Source.RequireCurrent());
        Console.WriteLine("OPENNV_EXPERIENCE_SOURCE_HUD_PASS actualRetainedTiles=true noRenderer=true constructorQueueEmpty=true " +
            "coldRawBitsAndStrings=true freshFactories=true oldTransitionUnchanged=true missingGradualRefused=true gameplay=UNEXECUTED");
    }

    private sealed class ExperienceSourceHudFixture : IDisposable
    {
        internal FalloutUiComponentStore Ui { get; }
        internal FalloutPlayerControlHudState Hud { get; }
        internal FalloutPlayerControlDisplayState Display { get; }
        internal FalloutStandaloneInterfaceState Interface { get; }
        internal FalloutExperienceSourceHud Source { get; }
        internal ControlHudCounter Counter { get; } = new();
        private readonly FalloutSourceFrameTimerState _timer;
        private readonly FalloutRendererClockState _clock;
        private readonly FalloutDirectInputState _input;
        private readonly FalloutPlayerThreadContextState _thread;
        private readonly FalloutPlayerControlHudContext _context;

        internal ExperienceSourceHudFixture(FalloutMainScriptCallerSource main, string stack,
            FalloutPlayerControlHudSnapshot? hudSaved = null, FalloutPlayerControlDisplaySnapshot? displaySaved = null,
            FalloutExperienceSourceHudSnapshot? sourceSaved = null)
        {
            var process = Guid.NewGuid(); var controls = FalloutPlayerMovementControlSource.Reviewed(main);
            var hudSource = FalloutPlayerControlHudSource.Reviewed(controls); var settings = AuthoredControlHudSettings(hudSource, stack);
            var fields = new FalloutPlayerControlFieldsState(controls, stack, process);
            _input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null)); fields.BindActualInput(_input);
            _thread = new(FalloutPlayerThreadContextSource.Reviewed(main), process);
            var timerSource = FalloutSourceFrameTimer.Read(new(main.EngineSha256, new('a', 64), new('b', 64), new('c', 64),
                new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
                new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
            _timer = new(timerSource, Counter, process);
            _clock = new(new(FalloutRendererClockSource.Reviewed(main), "authored-XP-source-INI",
                new(new("iUpdateType:HAVOK", FalloutIniCollection.Main, 0), 0, "authored-update"),
                new(new("fChaseDeltaMult:HAVOK", FalloutIniCollection.Main, 0x3d4ccccd), .05f, "authored-chase")), process);
            Interface = new(FalloutStandaloneInterfaceSource.Read(main), stack, process);
            Display = new(hudSource, stack, process, displaySaved);
            if (displaySaved is null)
            {
                Display.ApplyDeclaredConfiguration(settings);
                Display.PublishCurrentLayout(Guid.NewGuid(), "authored-XP-source-layout", () => fields.RequireActualInput(_input));
            }
            var menu = new XElement("menu", new XAttribute("name", "HUDMainMenu"),
                new XElement("visible", 0), new XElement("fadein", 0), new XElement("fadeout", 0),
                FalloutPlayerControlHudSource.VisibilityTiles.Select(name => new XElement("rect", new XAttribute("name", name),
                    new XElement("visible", 1), name == "XPMeter" ? FalloutExperienceHudSource.Tiles.Skip(1).Select(child =>
                        new XElement(child is "XPBracket" or "XPPointer" ? "image" : "text", new XAttribute("name", child),
                            new XElement("visible", 0), new XElement("alpha", 0), new XElement("string", "authored source string"))) : null)),
                Template("template_justify_center_text", "text", "justify_center_text"));
            Ui = FalloutUiComponentStore.Synthetic(new XElement(menu));
            _context = new(hudSource, stack, process, Interface, _timer, _clock, _input, fields);
            Counter.Now = 42;
            Hud = new(hudSource, stack, process, Ui, settings, _thread, _context, hudSaved, Counter.Read);
            if (hudSaved is null) Hud.Initialize(Display, fields.ReadDisabledWord);
            var declaration = new FalloutExperienceHudDeclaration(main.EngineSha256, new('d', 64),
                .3f, .7f, .4f, .2f, 500, 900, 31, 37, "+%i", "%i", "AuthoredOpacity", "AuthoredGain", "AuthoredLevel");
            Source = new(declaration, new('b', 64), menu, Ui, Hud, Display, sourceSaved);
        }
        internal void CompleteTransitions()
        { Counter.Now = 50; _timer.Update(); Interface.AdvanceHudMenuTransitions(_context.ReadTransitionFrame()); }
        public void Dispose() { Hud.Retire(); _thread.Retire(); Interface.Retire(); _input.Retire(); _timer.Dispose(); _clock.Dispose(); }
    }
}
