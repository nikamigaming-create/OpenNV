using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class RestMenuControlContracts
{
    internal static void Run()
    {
        foreach (var carry in new[] { true, false })
        {
            ActualTileOrder(carry, false); ActualTileOrder(carry, true);
            ColdCounting(carry); FaultPrefix(carry, FalloutRestMenuTarget.Action);
            FaultPrefix(carry, FalloutRestMenuTarget.Slider); MissingPublication(carry); NewRequestControls(carry);
        }
        Console.WriteLine("OPENNV_REST_MENU_CONTROLS_PASS authored=true originalTarget=true sourceVisibility=true " +
            "orderedBeforeHours=true currentColdNoReplay=true firstAndSecondPublicationFault=true " +
            "missingReceiptRefused=true native=unexecuted worldHour=unowned");
    }

    private sealed class Fixture
    {
        internal readonly FalloutGlobalState Globals;
        internal readonly FalloutGameTime Clock;
        internal readonly FalloutSleepWait Rest;
        internal readonly FalloutUiComponentStore Ui;
        internal readonly List<string> Order = [];
        internal bool Sleeping, Missing;
        internal FalloutRestMenuTarget? FailTarget;
        internal int StartCalls, ProjectionCalls, WorldHours;
        internal Fixture(bool carry, bool hideInSource = false, FalloutSleepWaitSnapshot? restore = null,
            FalloutGlobalStateSnapshot? globals = null, FalloutGameTimeSnapshot? clock = null)
        {
            var root = """
                <menu name="AuthoredRest">
                  <hotrect name="Action"><target>1</target><visible>1</visible>
                    <_line_alpha><copy>91</copy><add><copy>37</copy><onlyif src="me()" trait="target"/></add></_line_alpha>
                  </hotrect>
                  <hotrect name="Slider"><target>1</target><visible>1</visible>
                    <hotrect name="Pointer"><target><copy src="parent()" trait="target"/></target><visible>1</visible></hotrect>
                  </hotrect>
                  <hotrect name="Cancel"><target>1</target><visible>1</visible></hotrect>
                </menu>
                """;
            var sourceBytes = Encoding.UTF8.GetBytes(root); var parsed = FalloutMenuXml.Parse(sourceBytes).Elements("menu").Single();
            if (hideInSource) parsed.Elements().First(tile => (string?)tile.Attribute("name") == "Action").SetElementValue("visible", 0);
            Ui = FalloutUiComponentStore.Synthetic(parsed);
            var keys = Enumerable.Range(0, 6).Select(i => new FalloutFormKey("AuthoredRestControls.esm", (uint)(0x35 + i))).ToArray();
            float[] values = [2280, 3, 4, 12, 600.25f, 30];
            Globals = new(keys.Select((key, index) => new FalloutGlobal(key, "Authored" + index, (byte)'f', values[index], "authored")));
            Clock = new(Globals, new(keys[0], keys[1], keys[2], keys[3], keys[4], keys[5]),
                new([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], new('a', 64), carry, !carry));
            Clock.InitializeNewGame(); if (globals is not null) Globals.Restore(globals); if (clock is not null) Clock.Restore(clock);
            Sleeping = restore?.Sleeping ?? false;
            var source = new FalloutSleepWaitSource(carry ?
                "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" :
                "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e",
                new('1', 64), new('2', 64), FalloutSleepWaitSource.CurrentContractSha256, carry, carry, 24);
            Rest = new(source, Clock, new(
                (_, _) => new(FalloutRestFactState.Satisfied, "authored-world-and-admission"),
                _ =>
                {
                    StartCalls++; if (Missing) return;
                    Rest!.BeginSourceMenuCounting(PublishTarget);
                },
                (flag, write) =>
                {
                    Require(Rest!.Request!.Origin == FalloutRestOrigin.ScriptHours ||
                        Rest.RequireCurrentMenuControls().Capture() is { Counting: true, TargetWrites: 2, NativePublications: 2 },
                        "Player hours overtook actual source target writes.");
                    Order.Add("hours"); write(); Sleeping = flag;
                },
                _ => Order.Add("hour-prelude"), _ => WorldHours++, _ => Order.Add("effects"),
                _ => Order.Add("complete-sleep"), (_, _) => Order.Add("close"), () => Sleeping)
                { AfterMenuPlayerHours = _ => Order.Add("after-hours"), BeforeCancelPlayerHours = _ => Order.Add("cancel") }, restore);
        }
        private void PublishTarget(FalloutRestMenuTarget target)
        {
            ProjectionCalls++; Order.Add(target.ToString());
            Require(Rest.RequireCurrentMenuControls().OverridesTarget(target) && Rest.RemainingHours == 0,
                "Projection invented a target or ran after the signed counter write.");
            Require(Rest.SaveBlocker is not null && JsonSerializer.Serialize(Rest.State).Contains("TargetWrites", StringComparison.Ordinal),
                "In-flight target state lost factual telemetry or admitted an incomplete save.");
            Reject(() => Rest.Capture());
            var path = "AuthoredRest/" + target + "/target";
            Require(Ui.SetFloat(path, 0), "Authored actual tile was missing.");
            if (FailTarget == target) throw new IOException("authored-native-target-after-tile-mutation");
        }
        internal void Open()
        { Rest.Open(new(FalloutRestKind.Wait, FalloutRestOrigin.SourceCommand)); Rest.Publish(Rest.RequestOrdinal); Rest.Select(2); }
        internal Fixture Cold() => new(Rest.Source.CarryAtDayBoundary, restore: RoundTrip(Rest.Capture()),
            globals: RoundTrip(Globals.Capture()), clock: RoundTrip(Clock.Capture()));
        internal void ProjectColdValues()
        {
            var controls = Rest.RequireCurrentMenuControls(); controls.RequireHealthy();
            foreach (var target in new[] { FalloutRestMenuTarget.Action, FalloutRestMenuTarget.Slider })
                if (controls.OverridesTarget(target)) Require(Ui.SetFloat("AuthoredRest/" + target + "/target", 0), "Cold projection lost its actual tile.");
        }
    }
    private static void ActualTileOrder(bool carry, bool sourceHidden)
    {
        var fixture = new Fixture(carry, sourceHidden); fixture.Open();
        var visible = fixture.Ui.GetFloat("AuthoredRest/Action/visible"); fixture.Rest.Begin();
        Require(fixture.Order.SequenceEqual(new[] { "Action", "Slider", "hours", "after-hours" }), "Menu Start changed original target/player order.");
        Require(fixture.Ui.GetFloat("AuthoredRest/Action/visible") == visible && visible == (sourceHidden ? 0 : 1),
            "Menu counting replaced authored visibility.");
        Require(fixture.Ui.GetFloat("AuthoredRest/Action/_line_alpha") == 91 &&
            fixture.Ui.GetFloat("AuthoredRest/Slider/Pointer/target") == 0 && fixture.Ui.GetFloat("AuthoredRest/Cancel/target") == 1,
            "Target clear lost actual dependent traits or disabled the source cancellation target.");
        Require(fixture.Rest.Capture().MenuControls is { Counting: true, TargetWrites: 2, NativePublications: 2 },
            "Start retained no actual control receipt.");
    }
    private static void ColdCounting(bool carry)
    {
        var warm = new Fixture(carry); warm.Open(); warm.Rest.Begin(); warm.Rest.AdvanceCountdown(.625f);
        var cold = warm.Cold(); Require(cold.StartCalls == 0 && cold.ProjectionCalls == 0 && cold.WorldHours == 0,
            "Cold construction replayed source counting, projection or an hour.");
        cold.ProjectColdValues(); cold.Rest.Publish(cold.Rest.RequestOrdinal);
        Require(cold.Rest.RequireCurrentMenuControls().Capture() == warm.Rest.RequireCurrentMenuControls().Capture() &&
            cold.Ui.GetFloat("AuthoredRest/Action/visible") == 1 && cold.Ui.GetFloat("AuthoredRest/Action/target") == 0,
            "Cold projection hid an action or invented a new start.");
        warm.Rest.AdvanceCountdown(.375f); cold.Rest.AdvanceCountdown(.375f);
        Require(warm.Rest.Capture() == cold.Rest.Capture() ||
            JsonSerializer.Serialize(warm.Rest.Capture()) == JsonSerializer.Serialize(cold.Rest.Capture()),
            "Cold exact countdown changed the source hour/control prefix.");
    }
    private static void FaultPrefix(bool carry, FalloutRestMenuTarget target)
    {
        var fixture = new Fixture(carry) { FailTarget = target }; fixture.Open(); Reject(fixture.Rest.Begin);
        var state = RoundTrip(fixture.Rest.Capture()); var controls = state.MenuControls!;
        Require(state.Failure?.Step == FalloutRestStep.MenuBegin && state.RemainingHours == 0 && state.CommittedHours == 0 &&
            controls is { Counting: true, Attempt: 1 } && controls.TargetWrites == (int)target + 1 &&
            controls.NativePublications == (int)target && controls.Failure?.Target == target,
            "Native target failure erased or completed the actual committed source prefix.");
        var calls = fixture.ProjectionCalls; Reject(fixture.Rest.Begin); Require(fixture.ProjectionCalls == calls, "Warm target failure replayed.");
        var cold = fixture.Cold(); Reject(cold.Rest.Begin); Reject(cold.ProjectColdValues);
        Require(cold.StartCalls == 0 && cold.ProjectionCalls == 0 && JsonSerializer.Serialize(cold.Rest.Capture()) == JsonSerializer.Serialize(state),
            "Cold control failure replayed or dropped the source target prefix.");
        Reject(() => (state with { MenuControls = controls with { Request = controls.Request + 1 } }).Validate());
        Reject(() => (state with { MenuControls = controls with { NativePublications = 2 } }).Validate());
        Reject(() => (state with { MenuControls = controls with { SourceSha256 = new('0', 64) } }).Validate());
        Reject(() => (state with { MenuControls = controls with { TargetWrites = 0 } }).Validate());
        Reject(() => (state with { Failure = null }).Validate());
    }
    private static void MissingPublication(bool carry)
    {
        var fixture = new Fixture(carry) { Missing = true }; fixture.Open(); Reject(fixture.Rest.Begin);
        Require(fixture.Rest.Capture() is { RemainingHours: 0, Sleeping: false, Failure.Step: FalloutRestStep.MenuBegin,
            MenuControls.Counting: false }, "Empty Begin callback fabricated original target/counting completion.");
        var fresh = new Fixture(carry); fresh.Open();
        Reject(() => fresh.Rest.BeginSourceMenuCounting(_ => throw new InvalidOperationException("Outside the actual Start")));
        Require(fresh.Rest.RequireCurrentMenuControls().Capture() is { Counting: false, Attempt: 0 }, "Out-of-order controls changed constructor state.");
    }
    private static void NewRequestControls(bool carry)
    {
        var fixture = new Fixture(carry); fixture.Open(); fixture.Rest.Begin(); fixture.Rest.Cancel();
        var previous = fixture.Rest.RequireCurrentMenuControls(); var ordinal = fixture.Rest.RequestOrdinal;
        fixture.Rest.RetirePublication(); fixture.Open();
        Require(!ReferenceEquals(previous, fixture.Rest.RequireCurrentMenuControls()) &&
            fixture.Rest.RequireCurrentMenuControls().Capture() is { Counting: false, TargetWrites: 0, NativePublications: 0, Attempt: 0 } &&
            fixture.Rest.RequestOrdinal == ordinal + 1 && previous.Counting,
            "A new actual request reused or erased the previous committed menu-control owner.");
        var fresh = new Fixture(carry); Reject(() => fresh.Rest.Open(new(FalloutRestKind.Sleep, FalloutRestOrigin.ScriptHours)));
        Require(fresh.Rest.Capture().Request is null, "A script counter write allocated a fabricated menu owner.");
    }
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or IOException) { return; }
        throw new InvalidDataException("Required rest control refusal was absent.");
    }
}
