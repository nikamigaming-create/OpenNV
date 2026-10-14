using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class OriginalInputContracts
{
    internal static void Run()
    {
        TruthTable(); CountsAndWheel(); FailureLanes(); ConstructorAndCallerFailures(); ColdFresh(); CommandCadence();
        Console.WriteLine("Original input contracts PASS: bit7 edges/counts, swap/wheel, independent poll failures, hook ordering, caller guards and fresh cold history.");
    }
    private static void TruthTable()
    {
        int[][] expected = [[0, 0, 0, 0], [1, 1, 0, 1], [0, 0, 1, 1], [1, 0, 0, 0]];
        foreach (var previous in new byte[] { 0, 1, 0x7f, 0x80, 0xff })
            foreach (var current in new byte[] { 0, 1, 0x7f, 0x80, 0xff })
                for (var mode = 0; mode < 4; ++mode)
                {
                    var row = (previous < 0x80 ? 0 : 2) + (current < 0x80 ? 0 : 1);
                    Require(FalloutDirectInputState.Edge(current, previous, mode) == expected[row][mode], "Source edge confused full byte nonzero with bit7, or reversed transition direction.");
                }
    }
    private static void CountsAndWheel()
    {
        using var both = new Fixture();
        both.Devices.Keyboard[0x13] = 0x80; both.Devices.Mouse[12] = 0x80; both.Poll();
        var revision = both.State.CaptureSourceInput().BufferRevision; var sample = both.State.Sample;
        Require(both.Query(1).Count == 2 && both.Query(0).Count == 2 && both.Query(2).Count == 0 && both.Query(3).Count == 2,
            "Keyboard/mouse were ORed into a Boolean or rising/held/falling/either lost the actual count.");
        Require(both.Query(0).Observations.Select(value => value.Device).SequenceEqual([FalloutOriginalInputDevice.Keyboard, FalloutOriginalInputDevice.Mouse]) &&
            both.State.Sample == sample && both.State.CaptureSourceInput().BufferRevision == revision,
            "Source query reordered devices, shifted buffers or polled input.");
        both.Poll(); Require(both.Query(1).Count == 0 && both.Query(3).Count == 0, "Second held sample manufactured another rising/either edge.");
        both.Devices.Keyboard[0x13] = 0; both.Devices.Mouse[12] = 0; both.Poll();
        Require(both.Query(2).Count == 2 && both.Query(3).Count == 2 && both.Query(0).Count == 0, "Release sample omitted the two falling edges.");

        using var plain = new Fixture(managed: false);
        plain.Devices.Keyboard[0x13] = 1; plain.Devices.Mouse[12] = 1; plain.Poll();
        Require(plain.Query(0).Count == 0 && plain.State.Pressed(0x13, 2) && plain.State.CaptureSourceInput().Keyboard[0x13] == 1,
            "Plain native bytes were normalized by an unadmitted engine hook or raw cache was aliased to bit7.");
        using var swapped = new Fixture(keyboard: 255, mouse: 0, swap: 1);
        swapped.Devices.Mouse[13] = 0x80; swapped.Poll();
        Require(swapped.Query(1) is { Count: 1, Observations: [ { EffectiveButton: 1, Swap: 1 } ] }, "Mouse source swap did not select the actual current/previous paired button.");
        Require(swapped.Query(4).Count == 0, "Mode four was aliased to a held button.");

        using var wheel = new Fixture(keyboard: 255, mouse: 8);
        BinaryPrimitives.WriteInt32LittleEndian(wheel.Devices.Mouse.AsSpan(8), 120);
        wheel.State.SetDisable(FalloutInputLayout.WheelUp, true); wheel.Poll();
        wheel.State.RetainUnownedSourceWriter(wheel.Invocation, FalloutOriginalInputField.MouseSwap, "authored-unowned-swap");
        Require(wheel.Query(int.MinValue).Count == 1 && !wheel.State.Pressed(FalloutInputLayout.WheelUp) &&
            wheel.Query(99).Observations.Single().Wheel == 120, "Wheel read a button mode/swap or rewrote signed Z from a disabled hook cache.");
        wheel.State.Controls.Set(7, 9, 1); BinaryPrimitives.WriteInt32LittleEndian(wheel.Devices.Mouse.AsSpan(8), -120); wheel.Poll();
        Require(wheel.Query(99).Count == 1, "Negative wheel source direction was reversed or mode-filtered.");

        using var skipped = new Fixture(keyboard: 255, mouse: 255);
        skipped.Poll(); skipped.State.RetainUnownedSourceWriter(skipped.Invocation, FalloutOriginalInputField.MouseCurrent, "authored-unowned-current");
        Require(skipped.Query(0) is { Count: 0, Observations.Count: 0 }, "FF binding read an unowned device field.");
        using var adjacent = new Fixture(mouse: 10); adjacent.Devices.Keyboard[0x13] = 0x80; adjacent.Poll();
        Reject(() => adjacent.Query(0));
        Require(adjacent.State.LastSourceControlRead is { Count: 1, Step: FalloutOriginalInputQueryStep.Failed,
            FailedAt: FalloutOriginalInputQueryStep.Device, Observations: [{ Device: FalloutOriginalInputDevice.Keyboard }] }, "Failed second device discarded the first source count.");
    }
    private static void FailureLanes()
    {
        using var fixture = new Fixture();
        fixture.Devices.Keyboard[0x13] = 0x80; fixture.Devices.Mouse[12] = 0x80; fixture.Poll();
        fixture.State.Tap(0x13); fixture.State.Tap(FalloutInputLayout.MouseLeft);
        fixture.Devices.KeyboardAcquire = -1; fixture.Devices.MouseAcquire = -2; var failed = fixture.Poll();
        Require(failed.Returned && failed.Keyboard!.AcquireStatus == -1 && failed.Mouse!.AcquireStatus == -2 &&
            fixture.Query(2).Count == 2 && fixture.Query(0).Count == 0 && fixture.State.Pressed(0x13) && fixture.State.Pressed(FalloutInputLayout.MouseLeft) &&
            fixture.State.Capture().Keys[0x13].Tap, "Failed acquisition conflated zeroed engine current with preserved hook cache/pending tap.");
        fixture.Devices.KeyboardAcquire = 1; fixture.Devices.Keyboard[0x13] = 0;
        var partial = fixture.Poll();
        Require(partial.Keyboard is { AcquireStatus: 1, ReadStatus: 0, HookApplied: true } && partial.Mouse is { AcquireStatus: -2 } &&
            fixture.Query(0).Count == 1 && fixture.State.Capture().Keys[FalloutInputLayout.MouseLeft].Tap && !fixture.State.Capture().Keys[0x13].Tap,
            "Mouse failure rolled back keyboard success, consumed mouse tap, or rejected S_FALSE acquisition.");
        fixture.Devices.KeyboardRead = -3; fixture.Devices.Keyboard[0x13] = 1;
        fixture.State.Tap(0x13); fixture.Poll();
        Require(fixture.Query(0).Count == 0 && fixture.State.CaptureSourceInput().Keyboard[0x13] == 1 && fixture.State.Capture().Keys[0x13].Tap,
            "Ignored GetDeviceState HRESULT replaced actual output bytes or entered the hook/tap consumer.");
        fixture.Devices.WriteKeyboard = false; fixture.Devices.Keyboard[0x13] = 0x80; fixture.Poll();
        Require(fixture.State.CaptureSourceInput().Keyboard[0x13] == 1, "Failed no-write state output discarded the initialized actual destination.");

        using var hooks = new Fixture(keyboard: 0x13, mouse: 255);
        hooks.Devices.Keyboard[0x13] = 0x80; hooks.State.SetDisable(0x13, true, 1); hooks.State.Tap(0x13); hooks.Poll();
        Require(hooks.Query(0).Count == 1 && hooks.State.Capture().Keys[0x13] is { Raw: true, Game: true, Inserted: true, Tap: false },
            "User disable incorrectly blocked script tap insertion or successful tap consumption.");
        hooks.State.SetDisable(0x13, true, 2); hooks.State.Tap(0x13); hooks.Poll();
        Require(hooks.Query(0).Count == 0 && hooks.State.Capture().Keys[0x13] is { Raw: true, Game: false, Inserted: false, Tap: true },
            "Script disable consumed a pending tap or left effective engine bytes held.");
        hooks.State.SetDisable(0x13, false, 2); hooks.Poll(); Require(hooks.Query(1).Count == 1, "Reenabled pending tap failed to produce its real effective rising edge.");
        hooks.Platform.Status = 0; hooks.Poll(); Reject(() => hooks.Query(0));
        Require(hooks.State.LastSourceControlRead is { ControllerSelected: true, Observations.Count: 0 }, "Positive actual controller selection silently chose keyboard/mouse.");
        Require(hooks.Query(0, 31).Count == 0, "Source selected-controller large action did not take its actual zero arm.");
    }
    private static void ConstructorAndCallerFailures()
    {
        using var beforePoll = new Fixture(); Reject(() => beforePoll.Query(0));
        Require(beforePoll.State.LastSourceControlRead is { FailedAt: FalloutOriginalInputQueryStep.Selection }, "Unpolled current selector was inferred from constructor/headless state.");
        using var wrong = new Fixture(); wrong.Poll();
        Reject(() => wrong.State.ReadSourceControl(wrong.Invocation with { PlayerProcess = Guid.NewGuid() }, 7, 0));
        Require(wrong.State.LastSourceControlRead is { FailedAt: FalloutOriginalInputQueryStep.Entered }, "Foreign Player/Main call entered the source binding.");
        wrong.OnGuard = () => { try { wrong.State.Tap(0x13); } catch (InvalidOperationException) { } };
        Reject(() => wrong.Query(0)); Require(wrong.State.LastSourceControlRead!.Failure!.Contains("swallowed", StringComparison.Ordinal), "Caller swallowed reentry and published a source query.");
        wrong.OnGuard = null;
        wrong.Devices.OnRead = _ => { try { wrong.Poll(); } catch (InvalidOperationException) { } };
        Reject(() => wrong.Poll());
        Require(wrong.State.CaptureSourceInput() is { Known.Keyboard: false, Poll.Keyboard.FailedAt: FalloutOriginalInputPollStep.StateRead },
            "Swallowed device reentry promoted unowned output bytes.");
        using var constructor = new Fixture(bind: false);
        constructor.Platform.FailAfterSwap = true; Reject(constructor.Bind);
        Require(constructor.State.CaptureSourceInput() is { Construction: { Returned: false, Swap: 0, DoubleClickMilliseconds: null, Failure: not null }, Known.Swap: true },
            "Failed constructor discarded its earlier swap store or invented a double-click child return.");
        Require(constructor.State.CaptureSourceInput().Known is { Keyboard: false, PreviousKeyboard: false, Mouse: false,
            PreviousMouse: false, Suppression: false, MenuWord: true }, "Failed earlier platform child established unreached buffer/suppression stores or omitted preceding menu-word zero.");
        var partial = constructor.State.CaptureSourceInput();
        FalloutOriginalInputValidation.ValidateSnapshot(constructor.Source, constructor.State.Source, partial);
        Reject(() => FalloutOriginalInputValidation.ValidateSnapshot(constructor.Source, constructor.State.Source, partial with
            { Known = partial.Known with { Keyboard = true } }));
        var foreign = FalloutOriginalInputHookBinding.ManagedControls(wrong.State);
        using var another = new Fixture(bind: false); Reject(() => another.State.BindSourceControlQueries(another.Source, another.Platform, _ => { }, foreign));
    }
    private static void ColdFresh()
    {
        using var old = new Fixture(); old.Devices.Keyboard[0x13] = old.Devices.Mouse[12] = 0x80; old.Poll(); old.Query(0); old.State.Tap(0x13);
        var saved = RoundTrip(old.State.CaptureSourceInput()); FalloutOriginalInputValidation.ValidateSnapshot(old.Source, old.State.Source, saved);
        using var cold = new Fixture(swap: 1);
        cold.State.RetainHistoricalSourceInput(saved, invocation => Require(invocation == old.Invocation, "Cold input history lost its actual parent association."));
        Require(cold.State.Factory != saved.Factory && cold.State.Sample == 0 && cold.State.CaptureSourceInput() is
            { PollSequence: 0, QuerySequence: 0, Swap: 1, Query: null } && cold.State.HistoricalSourceInput!.Query!.Count == 2,
            "Cold history restored old native factory, bytes, queries or host swap state into the fresh provider.");
        Reject(() => cold.Query(0)); cold.Poll(); Require(cold.Query(0).Count == 0 && !cold.State.Capture().Keys[0x13].Tap,
            "Cold source query replayed old physical down or pending tap.");
        Reject(() => FalloutOriginalInputValidation.ValidateSnapshot(old.Source, old.State.Source, saved with { Keyboard = [] }));
        Reject(() => FalloutOriginalInputValidation.ValidateSnapshot(old.Source, old.State.Source, saved with { Query = saved.Query! with { Factory = Guid.NewGuid() } }));
        Reject(() => FalloutOriginalInputValidation.ValidateQuery(old.Source, old.State.Source, saved.Query! with { Count = 1 }));
    }
    private static void CommandCadence()
    {
        using var fixture = new Fixture(mouse: 255);
        var commands = new FalloutHeadlessInput(fixture.State);
        var edges = new List<(int Key, bool Down)>(); fixture.State.GameEdges += values => edges.AddRange(values);
        commands.Publish(); var initial = fixture.State.CaptureSourceInput(); var sample = fixture.State.Sample;
        commands.Key(0x13, true);
        var permission = fixture.State.ReadCommandEventPermission(0x13, true);
        Require(permission is { Allowed: true, RawDown: true, Controls.Game: false } && permission.Factory == fixture.State.Factory &&
            fixture.State.Sample == sample && fixture.State.CaptureSourceInput().BufferRevision == initial.BufferRevision &&
            fixture.Query(1).Count == 0 && edges.Count == 0,
            "Collected command/permission published a cache, shifted engine bytes or fabricated a source rising edge before the poll.");
        commands.Publish(); Require(fixture.Query(1).Count == 1 && edges.SequenceEqual([(0x13, true)]), "First real command poll lost its source rising edge or duplicated script down publication.");
        commands.Publish(); Require(fixture.Query(1).Count == 0 && fixture.Query(0).Count == 1 && edges.Count == 1, "Second normal poll manufactured a repeated rising edge.");
        commands.Key(0x13, false);
        Require(fixture.State.ReadCommandEventPermission(0x13, false).Allowed && fixture.Query(2).Count == 0 && fixture.State.Pressed(0x13),
            "Command release replaced current source/cache state before the normal poll.");
        commands.Publish(); Require(fixture.Query(2).Count == 1 && edges.SequenceEqual([(0x13, true), (0x13, false)]), "Actual release poll lost its source falling edge.");
        commands.Key(0x13, true); fixture.State.SetDisable(0x13, true, 1);
        Require(!fixture.State.ReadCommandEventPermission(0x13, true).Allowed, "Immediate command permission ignored current user disable.");
        commands.Publish(); Require(fixture.Query(0).Count == 0 && fixture.State.Capture().Keys[0x13].Raw, "Disabled physical command changed raw/effective engine separation.");
        fixture.State.Tap(0x13); fixture.State.SetDisable(0x13, true, 2);
        for (var index = 0; index < 3; ++index) Require(!fixture.State.ReadCommandEventPermission(0x13, true).Allowed, "Script-disabled pending tap admitted the command event.");
        Require(fixture.State.Capture().Keys[0x13].Tap, "Permission preview consumed a pending tap.");
        commands.Publish(); Require(fixture.Query(0).Count == 0 && fixture.State.Capture().Keys[0x13].Tap, "Successful script-disabled poll consumed tap.");
        fixture.State.SetDisable(0x13, false, 2); var beforePreview = fixture.State.CaptureSourceInput();
        for (var index = 0; index < 3; ++index) Require(fixture.State.ReadCommandEventPermission(0x13, true).Allowed, "Enabled pending insertion did not participate in managed UI permission.");
        Require(fixture.State.Capture().Keys[0x13].Tap && fixture.State.CaptureSourceInput().BufferRevision == beforePreview.BufferRevision && fixture.Query(0).Count == 0,
            "Repeated UI permission consumed tap or promoted predicted effective bytes to source authority.");
        commands.Publish(); Require(fixture.Query(1).Count == 1 && !fixture.State.Capture().Keys[0x13].Tap, "Real enabled poll did not consume exactly one tap and publish rising.");
        commands.Publish(); Require(fixture.Query(2).Count == 1 && fixture.Query(0).Count == 0, "Tap inserted into more than its one actual successful sample.");
        fixture.State.GameEdges += _ => throw new NotSupportedException("authored-command-script-callback-failure"); fixture.State.Tap(0x13);
        Reject(commands.Publish);
        Require(!fixture.State.Capture().Keys[0x13].Tap && fixture.State.CaptureSourceInput().Poll is { Returned: true } && fixture.Query(1).Count == 1,
            "Failed post-poll script callback rewound source buffers, tap consumption or the actual poll return.");
        Reject(commands.Publish); Require(fixture.Query(2).Count == 1 && fixture.Query(0).Count == 0, "Retry replayed the earlier consumed tap after callback failure.");
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly FalloutOriginalInputSource Source;
        internal readonly FalloutDirectInputState State;
        internal readonly Platform Platform;
        internal readonly Devices Devices;
        internal readonly FalloutInputInvocation Invocation = new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid());
        internal Action? OnGuard;
        private readonly bool _managed;
        internal Fixture(byte keyboard = 0x13, byte mouse = 0, int swap = 0, bool managed = true, bool bind = true)
        {
            var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
                FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
            Source = FalloutOriginalInputSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
            var controls = new FalloutInputControls(_ => null, newVegas: false,
                defaults: name => name == "Ready Item" ? new(keyboard, mouse) : new(255, 255));
            State = FalloutDirectInputState.CreateHeadless(new('3', 64), controls); Platform = new() { Swap = swap }; Devices = new(State); _managed = managed;
            if (bind) Bind();
        }
        internal void Bind() => State.BindSourceControlQueries(Source, Platform, value =>
        { Require(value == Invocation, "Foreign original input caller."); OnGuard?.Invoke(); },
            _managed ? FalloutOriginalInputHookBinding.ManagedControls(State) : FalloutOriginalInputHookBinding.PlainDevice(State));
        internal FalloutOriginalInputPoll Poll() => State.PollSourceDevices(Devices);
        internal FalloutOriginalInputQuery Query(int mode, int control = 7)
        {
            var query = State.ReadSourceControl(Invocation, control, mode);
            FalloutOriginalInputValidation.ValidateQuery(Source, State.Source, query); return query;
        }
        public void Dispose() => State.Retire();
    }
    private sealed class Platform : IFalloutOriginalInputPlatform
    {
        public Guid Factory { get; } = Guid.NewGuid();
        public string Owner => "authored platform fixture, no Windows capability claim";
        internal int Swap, Status = 1167;
        internal bool FailAfterSwap;
        public void RequireCurrent() { }
        public void ConstructMouse(Action<int> storeSwap, Action<uint> storeDoubleClickMilliseconds)
        { storeSwap(Swap); if (FailAfterSwap) throw new NotSupportedException("authored-double-click-child-unbound"); storeDoubleClickMilliseconds(500); }
        public int ReadControllerState(Span<byte> destination) { destination.Clear(); return Status; }
    }
    private sealed class Devices(FalloutDirectInputState owner) : IFalloutOriginalInputDevices
    {
        internal readonly byte[] Keyboard = new byte[256], Mouse = new byte[20];
        internal int KeyboardAcquire = 0, MouseAcquire = 0, KeyboardRead = 0, MouseRead = 0;
        internal bool WriteKeyboard = true;
        internal Action<FalloutOriginalInputDevice>? OnRead;
        public bool Present(FalloutOriginalInputDevice device) => true;
        public void RequireCurrent(FalloutDirectInputState current) { Require(ReferenceEquals(owner, current), "Authored device owner changed."); owner.ReadManagerOwner(); }
        public int Acquire(FalloutOriginalInputDevice device) => device == FalloutOriginalInputDevice.Keyboard ? KeyboardAcquire : MouseAcquire;
        public int Poll(FalloutOriginalInputDevice device) => -99; // Original ignores this status too.
        public int ReadState(FalloutOriginalInputDevice device, Span<byte> destination)
        {
            OnRead?.Invoke(device);
            if (device == FalloutOriginalInputDevice.Keyboard) { if (WriteKeyboard) Keyboard.CopyTo(destination); return KeyboardRead; }
            Mouse.CopyTo(destination); return MouseRead;
        }
    }
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or ObjectDisposedException) { return; }
        throw new InvalidOperationException("Original input accepted unsupported or incoherent current/cold state.");
    }
}
