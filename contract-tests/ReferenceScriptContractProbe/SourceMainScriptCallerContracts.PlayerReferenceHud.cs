using System.Text.Json;
using System.Xml.Linq;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerReferenceHudContracts()
    {
        RunPlayerThreadContextContracts();
        var immediate = new FalloutImmediateScriptSource(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3));
        var mainSource = FalloutMainScriptCallerSource.Read(immediate);
        var source = FalloutPlayerReferenceHudSource.Reviewed(mainSource);
        Reject(() => (source with { InitialColorBits = 0 }).Validate());
        Reject(() => (source with { Branch = "Info" }).Validate());
        var ui = AuthoredReferenceHudComponents();
        Require(ui.GetFloat("HUDMainMenu/childcount") == 1 && ui.GetFloat("HUDMainMenu/EnemyHealth/childcount") == 0,
            "UI template definitions became live components before a source factory call.");
        var process = Guid.NewGuid(); var main = Guid.NewGuid();
        var hud = new FalloutPlayerReferenceHudState(source, "authored-HUD-stack", process, ui);
        Require(ui.ReadComponentFloat(hud.Branch, "childcount") == 3 && ui.ReadComponentFloat(hud.Branch, "user0") == 2,
            "HUD constructor lost its three original template factories or separate color writer.");
        hud.Update(main, 1, null, 0);
        Require(hud.LastCall is { Step: FalloutPlayerReferenceHudStep.Returned, ColorBits: 0x40000000, Hostile: 0, DeliveredArgument: 0 } &&
            new[] { hud.Bracket, hud.Meter, hud.Name }.All(tile => ui.ReadComponentFloat(tile, "visible") == 1),
            "Source null reference hid unequal-color tiles or skipped the independent flag stores.");
        ui.WriteComponentFloat(hud.Branch, "user0", 1);
        hud.Update(main, 2, null, 255);
        Require(hud.LastCall is { Step: FalloutPlayerReferenceHudStep.Returned, ColorBits: 0x3f800000, DeliveredArgument: 255 } &&
            new[] { hud.Bracket, hud.Meter, hud.Name }.All(tile => ui.ReadComponentFloat(tile, "visible") == 0),
            "Ordered color equality failed to hide every actual template or narrowed the source argument byte.");
        foreach (var bits in new uint[] { 0, 0x80000000, 0x3f7fffff, 0x3f800001, 0x40000000, 0x7f800000, 0xff800000, 0x7fc00000, 0xffc00000 })
            Require(!FalloutPlayerReferenceHudSource.ClearsTiles(bits), "Different or unordered color selected the source equality stores.");
        Require(FalloutPlayerReferenceHudSource.ClearsTiles(0x3f800000), "Source comparison inverted its ordered equality arm.");
        Reject(() => hud.Update(main, 2, null, 0));
        Reject(() => hud.Update(Guid.NewGuid(), 3, null, 0));

        // Template construction shares the actual indexed source tree. A later
        // mod instance receives its real duplicate-name path, never an alias.
        var duplicate = ui.InstantiateTemplate(hud.Branch, source.MeterTemplate);
        Require(duplicate != hud.Meter && duplicate.EndsWith(":1", StringComparison.Ordinal),
            "Dynamic owned template construction reused the previous live UI component.");
        ui.WriteComponentFloat(hud.Meter, "visible", 1); ui.WriteComponentFloat(duplicate, "visible", 0);
        Require(ui.ReadComponentFloat(hud.Meter, "visible") == 1 && ui.ReadComponentFloat(duplicate, "visible") == 0,
            "A new template changed the prior source component identity.");

        foreach (var tile in new[] { hud.Bracket, hud.Meter, hud.Name }) ui.WriteComponentFloat(tile, "visible", 1);
        Require(ui.Unload(hud.Meter), "Fixture could not retire the actual meter component.");
        Reject(() => hud.Update(main, 3, null, 7));
        Require(hud.LastCall is { Step: FalloutPlayerReferenceHudStep.Failed, FailedAt: FalloutPlayerReferenceHudStep.MeterHide, DeliveredArgument: 255 } &&
            ui.ReadComponentFloat(hud.Bracket, "visible") == 0 && ui.ReadComponentFloat(hud.Name, "visible") == 1,
            "A missing actual HUD component concealed or reordered the committed hide prefix.");
        Reject(() => hud.Update(main, 4, null, 0)); hud.Retire();
        Reject(hud.RequireCurrent);

        var actorHud = new FalloutPlayerReferenceHudState(source, "authored-actor-HUD", Guid.NewGuid(), AuthoredReferenceHudComponents());
        Reject(() => actorHud.Update(Guid.NewGuid(), 1, new("authored.esm", 0x20), 1));
        Require(actorHud.LastCall is { FailedAt: FalloutPlayerReferenceHudStep.ReferenceGate, ColorBits: null },
            "The unowned actor/dead/hostility/value children were replaced by successful clear behavior."); actorHud.Retire();

        var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(mainSource), "authored-scene-HUD", Guid.NewGuid());
        var sceneHud = new FalloutPlayerReferenceHudState(source, scene.Stack, scene.Process, AuthoredReferenceHudComponents());
        var sceneMain = Guid.NewGuid(); scene.StoreSceneClock(sceneMain, 1, 0x3c800000); scene.EnterSceneVirtual(sceneMain, 1);
        Reject(() => scene.ExecuteInitialVirtualPrefix(sceneMain, 1, () => { }, sceneHud));
        var saved = scene.Capture();
        Require(saved.ReferenceHudCall is { Step: FalloutPlayerReferenceHudStep.Returned } &&
            saved.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.InputManagerQuery && saved.SceneBracket == 1 &&
            saved.ActorScalarReset is { Stores: 4, Returned: true } scalar && scalar.Fields.All(bits => bits == 0xbf800000) &&
            saved.ThreadContextEntry is { InitializerReturned: true, BeforeKind: 59, CurrentKind: 52, ScopeEntered: true } &&
            saved.ReferenceQuery is { Returned: true } && scene.SaveBlocker == "source-Player-scene-current-input-manager-product-factory-unbound",
            "Returning the actual HUD/reset/thread children skipped the independent input manager or certified a whole virtual return.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ActorScalarReset = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { ActorScalarReset = saved.ActorScalarReset! with { Fields = new uint?[] { 0xbf800000, 0, 0xbf800000, 0xbf800000 } } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ThreadContextEntry = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { ThreadContextEntry = saved.ThreadContextEntry! with { BeforeKind = 0 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ReferenceHudCall = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { ReferenceHudCall = saved.ReferenceHudCall! with { Source = new('a', 64) } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { ReferenceHudCall = saved.ReferenceHudCall! with { Process = Guid.NewGuid() } }));
        var serialized = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!;
        var revision = sceneHud.Components.Revision;
        var cold = new FalloutStandalonePlayerSceneState(scene.Source, scene.Stack, Guid.NewGuid(), serialized);
        Require(cold.Capture().ReferenceHudCall == saved.ReferenceHudCall && cold.Capture().ThreadContextEntry == saved.ThreadContextEntry &&
            sceneHud.Components.Revision == revision && cold.SaveBlocker == scene.SaveBlocker,
            "Cold failed-state restoration replayed the old HUD writers or promoted its old menu factory.");
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(cold.Capture()));
        cold.Retire(); scene.Retire(); sceneHud.Retire();
        RunPlayerInputManagerContracts(mainSource, source);
        RunPlayerInputManagerContracts(mainSource, source, headless: true);
        Console.WriteLine("OPENNV_PLAYER_REFERENCE_HUD_PASS sourceTemplateInstances=true orderedEquality=true independentByteStores=true partialFailureRetained=true actorScalarReset=true threadContext=true coldNoReplay=true targetActorChildren=UNOWNED wholePlayerVirtual=UNRETURNED");
    }
    private static void RunPlayerInputManagerContracts(FalloutMainScriptCallerSource mainSource, FalloutPlayerReferenceHudSource hudSource, bool headless = false)
    {
        var stack = new string('2', 64);
        var controls = new FalloutInputControls(_ => null);
        // Data-only device bytes are an explicit authored fixture. No HWND,
        // COM construction or real input acquisition is certified by this test.
        var input = headless ? FalloutDirectInputState.CreateHeadless(stack, controls) : new FalloutDirectInputState(stack, 123, controls);
        var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(mainSource), stack, Guid.NewGuid());
        var hud = new FalloutPlayerReferenceHudState(hudSource, stack, scene.Process, AuthoredReferenceHudComponents());
        var foreign = new FalloutDirectInputState(new('3', 64), 124, controls);
        Reject(() => scene.BindInputManager(foreign)); foreign.Retire();
        scene.BindInputManager(input); Reject(() => scene.BindInputManager(input));
        Exception? wrongThread = null;
        var thread = new Thread(() => { try { input.ReadManagerOwner(); } catch (Exception error) { wrongThread = error; } });
        thread.Start(); thread.Join(); Require(wrongThread is InvalidOperationException, "Input getter accepted a foreign device thread.");
        input.Publish(new byte[256], new byte[8], 7, 8, 9); input.DeviceUnavailable("authored-lost-acquisition");
        var bytes = input.Capture();
        var main = Guid.NewGuid(); scene.StoreSceneClock(main, 1, 0x3c800000); scene.EnterSceneVirtual(main, 1);
        Reject(() => scene.ExecuteInitialVirtualPrefix(main, 1, () => { }, hud));
        var saved = scene.Capture();
        Require(saved.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.ActorProcessQuery &&
            saved.InputManagerQuery is { Returned: true } query && query.Factory == input.Factory && query.Sample == 1 &&
            query.Revision == bytes.Revision && ReferenceEquals(input.ReadManagerOwner(), input) &&
            query.Provider == input.Provider && query.Window == input.Window &&
            input.Capture().MouseX == 7 && input.Sample == 1 && input.Failure == "authored-lost-acquisition",
            "Pure current input-manager getter polled, changed unavailable cached observations or skipped the Actor process virtual.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { InputManagerQuery = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { InputManagerQuery = saved.InputManagerQuery! with { Factory = Guid.Empty } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { InputManagerQuery = saved.InputManagerQuery! with { Provider = (FalloutInputProvider)99 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { InputManagerQuery = saved.InputManagerQuery! with { Window = headless ? 123UL : 0 } }));
        var serialized = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!;
        var cold = new FalloutStandalonePlayerSceneState(scene.Source, stack, Guid.NewGuid(), serialized);
        Require(cold.Capture().InputManagerQuery == saved.InputManagerQuery && input.Sample == 1,
            "Cold input receipt restoration rebound the previous live device or sampled it again.");
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(cold.Capture()));
        cold.Retire(); scene.Retire(); hud.Retire(); input.Retire(); Reject(() => input.ReadManagerOwner());
        Console.WriteLine("OPENNV_PLAYER_INPUT_MANAGER_PASS sameCSharpOwner=true getterNoPoll=true acquisitionFailurePreserved=true foreignSourceAndThreadRefused=true coldNoDeviceReplay=true nativeDevice=FIXTURE wholePlayerVirtual=UNRETURNED");
    }
    private static FalloutUiComponentStore AuthoredReferenceHudComponents() => FalloutUiComponentStore.Synthetic(
        new XElement("menu", new XAttribute("name", "HUDMainMenu"),
            new XElement("rect", new XAttribute("name", "EnemyHealth"), new XElement("user0", 0)),
            Template("template_enemy_health_bracket", "image", "enemy_health_bracket"),
            Template("template_meter", "rect", "meter"),
            Template("template_justify_center_text", "text", "justify_center_text")));
    private static XElement Template(string name, string kind, string tile) => new("template", new XAttribute("name", name),
        new XElement(kind, new XAttribute("name", tile), new XElement("visible", 1)));
}
