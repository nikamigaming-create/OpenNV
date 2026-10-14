using System.Text.Json;
using System.Xml.Linq;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class SourceMenuLifecycleContracts
{
    internal static void Run()
    {
        StackOrdering(); MenuRootLifetimes(); NativeFieldsAndCold(); CloseInputCount(); ReturnedCloseTail(); FailedWriterAndNativeRetirement();
        SourceMainScriptCallerContracts.RunHeldPlayerMenuContracts();
        Console.WriteLine("OPENNV_SOURCE_MENU_LIFECYCLE_PASS authored=true sourceWordsShared=true contextPreserved=true " +
            "mode3And4And1=true realKind13=true sourceImageByte=true noCensusStores=true inputMenuWord=true " +
            "coldCycles=3 callbacksNotReplayed=true originalExecution=UNEXECUTED pixels=UNMEASURED");
    }
    private static void MenuRootLifetimes()
    {
        var ui = FalloutUiComponentStore.Synthetic(
            XElement.Parse("<menu name='StartMenu'><close>0</close><rect name='Choice'><visible>1</visible></rect></menu>"),
            XElement.Parse("<menu name='HUDMainMenu'><visible>1</visible></menu>"));
        var old = ui.BindMenuRoot("StartMenu"); var child = ui.BindComponent("StartMenu/Choice");
        var hud = ui.BindMenuRoot("HUDMainMenu"); ui.StoreFloat(old, "close", 1); ui.StoreFloat(child, "visible", 0);
        ui.StoreFloat(hud, "visible", .5f);
        var fresh = ui.RecreateMenuRoot("StartMenu");
        Require(fresh.Factory == old.Factory && fresh.MenuInstance != old.MenuInstance && ui.ReadFloat(fresh, "close") == 0 &&
            ui.ReadFloat(ui.BindComponent("StartMenu/Choice"), "visible") == 1 && ui.ReadFloat(hud, "visible") == .5f,
            "Fresh menu root retained old close/child state or invalidated the independent current HUD.");
        Reject(() => ui.RequireComponent(old)); Reject(() => ui.RequireComponent(child));
        Require(!ui.CaptureComponentValues([old]).Single().Alive && ui.CaptureComponentValues([hud]).Single().Alive,
            "Cold component capture promoted an obsolete menu instance or retired another menu.");
    }
    private static void StackOrdering()
    {
        var slots = new uint[10];
        var hud = FalloutStandaloneMenuStack.Push(slots, 1, 1001);
        Require(hud is { Mode: 3, Result: 0, ImageMenuByte: null }, "First HUD push incorrectly changed the independent image byte.");
        var pause = FalloutStandaloneMenuStack.Push(hud.Slots, hud.Mode, 1013);
        Require(pause.Result == 1 && pause.ImageMenuByte == 1 && pause.Slots.Take(3).SequenceEqual([1001u, 1013u, 0u]),
            "Pause behind the sole HUD lost its actual first-empty/image arm.");
        var buried = FalloutStandaloneMenuStack.Push(pause.Slots, pause.Mode, 1009);
        var refused = FalloutStandaloneMenuStack.Remove(buried.Slots, buried.Mode, 1013, false);
        Require(refused.Result == -2 && refused.Slots.SequenceEqual(buried.Slots), "Ordinary removal discarded a newer source menu.");
        var forced = FalloutStandaloneMenuStack.Remove(buried.Slots, buried.Mode, 1013, true);
        Require(forced.Slots.Take(3).SequenceEqual([1001u, 1009u, 0u]) && forced.Mode == 3,
            "Forced source removal changed the relative order of surviving menus.");
        var single = FalloutStandaloneMenuStack.Push(slots, 1, 1013);
        var last = FalloutStandaloneMenuStack.Remove(single.Slots, single.Mode, 1013, true);
        Require(last is { Mode: 4, Result: 0, ImageMenuByte: 1 } && last.Slots.All(value => value == 0),
            "Last Pause removal manufactured quiet mode or reversed its source image byte.");
        var full = Enumerable.Range(1001, 10).Select(value => (uint)value).ToArray();
        Require(FalloutStandaloneMenuStack.Push(full, 3, 1013).Result == -1, "An exhausted ten-word source stack grew storage.");
        Reject(() => FalloutStandaloneMenuStack.Remove(full, 3, full[^1], true));
        Require(FalloutStandaloneMenuStack.Remove(full, 3, full[^1], true, 0).Slots[^1] == 0,
            "Source-owned adjacent zero was not consumed by its actually reached full-stack tail.");
        Require(FalloutStandaloneMenuLifecycleSource.Visible(0x7fc00000) && !FalloutStandaloneMenuLifecycleSource.Visible(0x80000000),
            "Source Float32 nonzero visibility treated NaN or signed zero as a generic Boolean bit.");
    }
    private static void NativeFieldsAndCold()
    {
        var fixtures = new List<Fixture>();
        try
        {
            var first = new Fixture(); fixtures.Add(first);
            first.ValidateRootBinding();
            var menu = first.Open();
            var open = first.State.Capture();
            var openImages = first.Images.CaptureSourceMainImages();
            var openScopes = first.Thread.CaptureKindScopes();
            AssertOwnerJoins(open, openImages.MenuStack!, openScopes);
            var push = open.MenuFields!.Writes.Single(row => row.ImageReturned);
            Reject(() => FalloutStandaloneInterfaceState.Validate(ReplaceWrite(open, push with { ImageWrites = [] })));
            Require(open.Mode == 3 && open.Context == 0 && open.MenuSlots[0] == 1013 && open.ActiveMenus[12] == 1 &&
                first.State.Read(FalloutStandaloneInterfaceQuery.MenuGate) == 1 && first.State.Read(FalloutStandaloneInterfaceQuery.GuiModeTwo) == 0 &&
                first.Images.CaptureSourceMenuStackEffect() is { Enabled: 1, Progress: null } &&
                first.Thread.CaptureKindScopes() is [{ EnteredKind: 13, Returned: true, BeforeKind: 59, RestoredKind: 59 }],
                "Source Pause fields or the exact independent image/TLS owner were inferred from presentation.");
            FalloutStandaloneInterfaceState.RequireSaving(open); Reject(() => FalloutStandaloneInterfaceState.RequirePlayable(open));
            Reject(() => FalloutStandaloneInterfaceState.Validate(open with { Context = 2 }));
            Reject(() => FalloutStandaloneInterfaceState.Validate(open with { Mode = 1 }));
            Reject(() => FalloutStandaloneInterfaceState.Validate(open with { ActiveMenus = new byte[60] }));
            first.Close(menu);
            var closed = first.State.Capture();
            var remove = closed.MenuFields!.Writes.Single(row => row.Operation == FalloutStandaloneMenuFieldOperation.ForceRemove);
            Reject(() => FalloutStandaloneInterfaceState.Validate(ReplaceWrite(closed, remove with { InputClose = null })));
            Reject(() => FalloutStandaloneInterfaceState.Validate(ReplaceWrite(closed,
                remove with { InputClose = remove.InputClose! with { Source = new('f', 64) } })));
            Require(closed.Mode == 4, "Menu destructor skipped the independently returned Interface mode4 prefix.");
            first.State.ReturnClosedMenuMode(Guid.NewGuid(), menu);
            var quiet = first.State.Capture(); FalloutStandaloneInterfaceState.RequirePlayable(quiet);
            var tail = quiet.MenuFields!.Writes[^1];
            Reject(() => FalloutStandaloneInterfaceState.Validate(ReplaceWrite(quiet, tail with { MouseFlush = null })));
            Require(quiet.Mode == 1 && quiet.ActiveMenus.All(value => value == 0) && quiet.MenuSlots.All(value => value == 0),
                "Actual mouse-buffer return did not restore quiet Interface fields.");

            var saved = RoundTrip(open); var imageSaved = RoundTrip(openImages); var scopeSaved = RoundTrip(openScopes); var calls = saved.MenuFields!.Writes.Count;
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                var cold = new Fixture(saved, imageSaved, scopeSaved); fixtures.Add(cold);
                var current = cold.State.Capture(); FalloutStandaloneInterfaceState.RequireSaving(current);
                Require(current.Mode == 1 && current.ActiveMenus.All(value => value == 0) && current.MenuSlots.All(value => value == 0) &&
                    current.MenuFields!.Writes.Count == calls && current.MenuFields.ColdConstructors.Count == cycle + 1 &&
                    cold.Thread.CaptureKindScopes().SequenceEqual(scopeSaved),
                    "Cold source menu promoted old native pointers or replayed constructor/push/input/thread callbacks.");
                var image = cold.Images.CaptureSourceMenuStackEffect();
                FalloutStandaloneMenuSaveContract.Validate(current, image, cold.Thread.CaptureKindScopes());
                if (current.MenuFields!.ColdConstructors.Count > 2)
                {
                    var disconnected = current.MenuFields.ColdConstructors.ToArray();
                    disconnected[1] = disconnected[1] with { PreviousProcess = Guid.NewGuid(), PreviousConstructor = Guid.NewGuid() };
                    Reject(() => FalloutStandaloneMenuSaveContract.Validate(current with
                    { MenuFields = current.MenuFields with { ColdConstructors = disconnected } }, image, cold.Thread.CaptureKindScopes()));
                }
                Require(image is { Enabled: null, Progress: null } && image.Constructors.Count == cycle + 2 &&
                    image.Writes.Count == imageSaved.MenuStack!.Writes.Count,
                    "Cold effect promoted old unknown/current values or replayed its ImageSpace stores.");
                saved = RoundTrip(current); imageSaved = RoundTrip(cold.Images.CaptureSourceMainImages());
            }
        }
        finally { foreach (var fixture in fixtures) fixture.Dispose(); }
    }
    private static FalloutStandaloneInterfaceSnapshot ReplaceWrite(FalloutStandaloneInterfaceSnapshot saved,
        FalloutStandaloneMenuFieldWrite replacement) => saved with
        { MenuFields = saved.MenuFields! with { Writes = saved.MenuFields!.Writes.Select(row => row.Invocation == replacement.Invocation ? replacement : row).ToArray() } };

    private static void AssertOwnerJoins(FalloutStandaloneInterfaceSnapshot saved, FalloutSourceMenuImageFields images,
        IReadOnlyList<FalloutSourceThreadKindScope> scopes)
    {
        FalloutStandaloneMenuSaveContract.Validate(saved, images, scopes);
        var imageCall = saved.MenuFields!.Writes.Single(row => row.ImageReturned);
        var image = imageCall.ImageWrites!.Single();
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(ReplaceWrite(saved, imageCall with
        { ImageWrites = [image with { Factory = Guid.NewGuid() }] }), images, scopes));
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(ReplaceWrite(saved, imageCall with
        { ImageWrites = [image with { Interface = Guid.NewGuid() }] }), images, scopes));
        var allocationCall = saved.MenuFields!.Writes.Single(row => row.Allocation is not null);
        var allocation = allocationCall.Allocation!;
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(ReplaceWrite(saved, allocationCall with
        { Allocation = allocation with { OwnerFactory = Guid.NewGuid() } }), images, scopes));
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(ReplaceWrite(saved, allocationCall with
        { Allocation = allocation with { Process = Guid.NewGuid() } }), images, scopes));
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(saved, null, scopes));
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(saved, images, []));
    }

    private static void CloseInputCount()
    {
        using var fixture = new Fixture(); fixture.Devices.Keyboard[1] = fixture.Devices.Mouse[12] = 0x80;
        fixture.Input.PollSourceDevices(fixture.Devices);
        var sample = fixture.Input.Sample; var menu = fixture.Open(); fixture.Close(menu);
        var input = fixture.Input.CaptureSourceInput();
        Require(input.MenuClose is { Count: 2, Returned: true } && input.MenuClose.Observations.Select(row => row.Device)
            .SequenceEqual([FalloutOriginalInputDevice.Keyboard, FalloutOriginalInputDevice.Mouse]) && input.MenuWord == 8 && fixture.Input.Sample == sample,
            "Menu close fabricated an input sample or combined actual device counts into a Boolean.");
        FalloutOriginalInputValidation.ValidateSnapshot(fixture.InputSource, fixture.Input.Source, input);
        Reject(() => FalloutOriginalInputValidation.ValidateSnapshot(fixture.InputSource, fixture.Input.Source, input with { MenuWord = 0 }));
        Reject(() => FalloutOriginalInputValidation.ValidateSnapshot(fixture.InputSource, fixture.Input.Source,
            input with { MenuClose = input.MenuClose! with { Count = 1 } }));
        fixture.State.ReturnClosedMenuMode(Guid.NewGuid(), menu);
        Require(fixture.Input.CaptureSourceInput().MenuMouseFlushes is [{ Present: false, Returned: true }],
            "Explicit authored absent-device branch manufactured a native DirectInput mouse.");
        var saved = fixture.State.Capture(); var images = fixture.Images.CaptureSourceMenuStackEffect(); var scopes = fixture.Thread.CaptureKindScopes();
        FalloutStandaloneMenuSaveContract.Validate(saved, images, scopes);
        var closeCall = saved.MenuFields!.Writes.Single(row => row.InputClose is not null);
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(ReplaceWrite(saved, closeCall with
        { InputClose = closeCall.InputClose! with { InputFactory = Guid.NewGuid() } }), images, scopes));
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(ReplaceWrite(saved, closeCall with
        { InputClose = closeCall.InputClose! with { Count = 1 } }), images, scopes));
        var flushCall = saved.MenuFields.Writes.Single(row => row.MouseFlush is not null);
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(ReplaceWrite(saved, flushCall with
        { MouseFlush = flushCall.MouseFlush! with { InputFactory = Guid.NewGuid() } }), images, scopes));
        Reject(() => FalloutStandaloneMenuSaveContract.Validate(saved with { MenuFields = saved.MenuFields with { Inputs = [] } }, images, scopes));
    }

    private static void FailedWriterAndNativeRetirement()
    {
        using var fixture = new Fixture(); var menu = fixture.Open();
        fixture.DuringFrame = () => { Reject(() => fixture.State.Capture()); };
        Reject(() => fixture.State.AdvanceStartMenuFields(Guid.NewGuid(), menu));
        var failed = fixture.State.Capture();
        Require(failed.MenuFields!.Writes[^1] is { Returned: false, Failure: not null } && fixture.State.SaveBlocker is not null,
            "A swallowed menu callback reentry erased its real failed prefix.");
        Reject(() => fixture.State.ReplaceStartMenuTransition(Guid.NewGuid(), menu));
        fixture.State.RetireFailedStartMenuFields(menu);
        var retired = fixture.State.Capture();
        Require(retired.MenuFields!.Menus[^1].Retired is not null && retired.MenuFields.Writes[^1].Failure is not null,
            "Failed menu/native cleanup lost the entered source writer or fabricated a returned close.");
        Reject(() => FalloutStandaloneInterfaceState.RequireSaving(retired));
    }

    private static void ReturnedCloseTail()
    {
        using var fixture = new Fixture();
        FalloutStandaloneInterfaceObject? retained = fixture.Open();
        fixture.Close(retained);
        Require(fixture.State.Capture().Mode == 4 && retained is not null &&
            !fixture.State.HasCurrentNativeMenu(1013, retained.Native.NativeInstance),
            "Native menu release discarded its retained source close tail or kept a live menu field.");
        Require(RuntimeNativeStandaloneInterface.CompleteReturnedMenuCloseTail(fixture.State, ref retained) &&
            retained is null && fixture.State.Capture().Mode == 1 &&
            fixture.Input.CaptureSourceInput().MenuMouseFlushes is [{ Returned: true }],
            "The next node-absent Interface update failed to flush actual input before clearing its logical receipt.");
        Require(!RuntimeNativeStandaloneInterface.CompleteReturnedMenuCloseTail(fixture.State, ref retained) &&
            fixture.Input.CaptureSourceInput().MenuMouseFlushes!.Count == 1,
            "A later quiet update replayed the returned menu/input close tail.");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly FalloutStandaloneInterfaceState State;
        internal readonly FalloutImageSpaceState Images = new();
        internal readonly FalloutPlayerThreadContextState Thread;
        internal readonly FalloutDirectInputState Input;
        internal readonly FalloutOriginalInputSource InputSource;
        internal readonly Devices Devices;
        private readonly FalloutUiComponentStore _ui;
        private ulong _native = 1;
        internal Action? DuringFrame;
        private bool _living = true;
        internal Fixture(FalloutStandaloneInterfaceSnapshot? saved = null, FalloutSourceImageSpaceMainSnapshot? images = null,
            IReadOnlyList<FalloutSourceThreadKindScope>? scopes = null)
        {
            var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
                FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
            var source = FalloutStandaloneInterfaceSource.Read(main); var process = Guid.NewGuid();
            State = new(source, new('3', 64), process, saved);
            State.PublishManager(Publication(FalloutStandaloneInterfaceObjectRole.PipBoyManager), () => _living);
            Thread = new(FalloutPlayerThreadContextSource.Reviewed(main), process);
            Thread.RetainColdKindScopes(scopes);
            InputSource = FalloutOriginalInputSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
            Input = FalloutDirectInputState.CreateHeadless(State.Stack, new(_ => null, newVegas: false,
                defaults: name => name == "Menu Mode" ? new(1, 0) : new(255, 255)));
            Input.BindSourceControlQueries(InputSource, new Platform(), _ => { }, FalloutOriginalInputHookBinding.PlainDevice(Input));
            Devices = new(Input); Input.PollSourceDevices(Devices);
            Input.BindSourceMenuMouseBufferFlush(call => new(call.Identity, call.Process, call.Object, Input.Factory,
                Input.Source, null, false, 20, null, null, null, null, true));
            var words = Enumerable.Repeat(Bits(1), 38).ToArray(); words[32] = words[36] = words[37] = 0;
            Images.ConfigureSourceMainImages(main, process, new(words, new('a', 64)), new(0, Bits(600),
                [0, Bits(1), Bits(1), 0, Bits(1), Bits(1000), 0, Bits(10000), 0, 0, 0, 0],
                "fRenderDepthMaxDepth:Imagespace", Bits(10000), new('b', 64)), images);
            State.BindMenuLifecycles(FalloutStandaloneMenuLifecycleSource.Reviewed(source), Images, Input, Thread,
                () => { if (!_living) throw new ObjectDisposedException("authored-native-interface"); },
                () => { DuringFrame?.Invoke(); return new(new(main.EngineSha256, process, 1, 0, Bits(.1f), true), 0, null); });
            _ui = FalloutUiComponentStore.Synthetic(XElement.Parse("<menu name='StartMenu'><class>entity_StartMenu</class>" +
                "<stackingtype>entity_no_click_past</stackingtype><visible>0</visible><fadein>0.25</fadein><fadeout>0.25</fadeout></menu>"));
        }
        internal FalloutStandaloneInterfaceObject Open()
        {
            var menu = State.ConstructStartMenuFields(Publication(FalloutStandaloneInterfaceObjectRole.OtherMenu), () => _living);
            State.BindStartMenuRoot(Guid.NewGuid(), menu, _ui, _ui.BindMenuRoot("StartMenu"));
            State.StoreStartMenuVisible(Guid.NewGuid(), menu, Bits(1)); State.ReplaceStartMenuTransition(Guid.NewGuid(), menu);
            return menu;
        }
        internal void ValidateRootBinding()
        {
            var root = _ui.BindMenuRoot("StartMenu");
            var copy = _ui.CopyComponent(root);
            Require(copy.Element("class")?.Value == "entity_StartMenu", "A bound root copied another current source tree.");
            Reject(() => _ui.CopyComponent(root with { Factory = Guid.NewGuid() }));
            Reject(() => _ui.CopyComponent(root with { Epoch = root.Epoch + 1 }));
            copy.Element("class")!.Value = "authored-local-copy-only";
            Require(_ui.CopyComponent(root).Element("class")?.Value == "entity_StartMenu",
                "A source component copy became a second mutable UI authority.");
        }
        internal void Close(FalloutStandaloneInterfaceObject menu)
        {
            State.ReplaceStartMenuTransition(Guid.NewGuid(), menu, closing: true);
            for (var frame = 0; frame < 10 && State.HasMenuFieldTransition(menu.Identity); ++frame)
                State.AdvanceStartMenuFields(Guid.NewGuid(), menu);
            State.CloseStartMenuFields(Guid.NewGuid(), menu); State.RetireStartMenuFields(menu);
        }
        private FalloutStandaloneInterfaceNativePublication Publication(FalloutStandaloneInterfaceObjectRole role) =>
            new(Guid.NewGuid(), State.Process, State.Source.Identity, State.Stack, role, _native++,
                role == FalloutStandaloneInterfaceObjectRole.OtherMenu ? FalloutStandaloneMenuLifecycleSource.StartMenuPath : "menus/main/hud_main_menu.xml",
                new('a', 64), "actual-authored-menu-instance");
        public void Dispose()
        {
            foreach (var row in State.Capture().Objects.Where(row => row.Process == State.Process && row.Role == FalloutStandaloneInterfaceObjectRole.OtherMenu && row.Retired is null))
                State.RetireFailedStartMenuFields(row);
            State.Retire(); _living = false; Input.Retire();
        }
    }
    private sealed class Platform : IFalloutOriginalInputPlatform
    {
        public Guid Factory { get; } = Guid.NewGuid();
        public string Owner => "authored menu platform";
        public void RequireCurrent() { }
        public void ConstructMouse(Action<int> swap, Action<uint> click) { swap(0); click(500); }
        public int ReadControllerState(Span<byte> destination) { destination.Clear(); return 1167; }
    }
    private sealed class Devices(FalloutDirectInputState input) : IFalloutOriginalInputDevices
    {
        internal readonly byte[] Keyboard = new byte[256], Mouse = new byte[20];
        public bool Present(FalloutOriginalInputDevice device) => true;
        public void RequireCurrent(FalloutDirectInputState owner) { Require(ReferenceEquals(owner, input), "Input fixture changed its actual owner."); }
        public int Acquire(FalloutOriginalInputDevice device) => 0;
        public int Poll(FalloutOriginalInputDevice device) => 0;
        public int ReadState(FalloutOriginalInputDevice device, Span<byte> destination)
        { (device == FalloutOriginalInputDevice.Keyboard ? Keyboard : Mouse).CopyTo(destination); return 0; }
    }
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Menu accepted an unowned source field or impossible source ordering.");
    }
}
