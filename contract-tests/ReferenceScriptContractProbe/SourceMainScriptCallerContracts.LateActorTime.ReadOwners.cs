using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunLateActorTimeReadOwnerContracts()
    {
        using var fixture = new LateActorTimeFixture();
        fixture.Enter();
        var actor = fixture.Capture(); var fields = RoundTripPackage(fixture.Base.Fields.Capture());
        var updates = RoundTripPackage(fixture.Updates.Capture()); var common = RoundTripPackage(fixture.Base.Common.Capture());
        var globals = RoundTripPackage(fixture.Globals.Capture());
        Validate(actor, fields, globals, updates);
        var call = actor.LastCall!; var flags = call.ReferenceFlags!; var day = call.CalendarDay!;
        Reject(() => Validate(actor with { LastCall = call with { ReferenceFlags = flags with { Source = new('e', 64) } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { ReferenceFlags = flags with { Factory = Guid.NewGuid() } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { ReferenceFlags = flags with { FieldsFactory = Guid.NewGuid() } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { ReferenceFlags = flags with { Sequence = flags.Sequence + 1 } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { CalendarDay = day with { Form = fixture.Forms.Hour } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { CalendarDay = day with { WinningSha256 = new('e', 64) } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { CalendarDay = day with { Bits = Bits(2), SignedDay = 2 } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { CalendarDay = day with { Owner = null } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { CalendarDay = day with { Owner = day.Owner! with { Factory = Guid.NewGuid() } } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { CalendarDay = day with { Owner = day.Owner! with { Sequence = day.Owner!.Sequence + 1 } } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { CalendarDay = day with { Owner = day.Owner! with { ValueRevision = 1 } } } }, fields, globals, updates));
        Reject(() => Validate(actor with { LastCall = call with { CalendarDay = day with { Owner = day.Owner! with { Caller = null! } } } }, fields, globals, updates));
        Reject(() => Validate(actor, fields, globals with { PlayerFrameCalendar = null }, updates));
        Reject(() => Validate(actor, fields, globals, updates with { PlayerFrameGlobalAi = null }));
        Reject(() => Validate(actor, fields, globals, updates with
        { PlayerFrameGlobalAi = updates.PlayerFrameGlobalAi! with { Publications = [] } }));
        var calendar = globals.PlayerFrameCalendar!;
        Reject(() => Validate(actor, fields, globals with { PlayerFrameCalendar = calendar with
        { Publications = [calendar.Publications[0] with { Process = Guid.NewGuid() }] } }, updates));
        Reject(() => Validate(actor, fields, globals with { PlayerFrameCalendar = calendar with
        { LastRead = calendar.LastRead! with { Bits = Bits(2), SignedDay = 2 } } }, updates));
        Reject(() => FalloutPlayerLateActorTimeSaveContract.Validate(actor, fields, globals, updates, fixture.Forms.Hour));

        // A real later day write must preserve the earlier read, including bits.
        fixture.Globals.Set(fixture.Forms.Day, 2);
        globals = RoundTripPackage(fixture.Globals.Capture());
        Require(globals.PlayerFrameCalendar!.LastRead == day && globals.PlayerFrameCalendar.ValueRevision == 1 &&
            globals.Values.Single(row => row.Form == fixture.Forms.Day).Value == 2,
            "A later real GLOBAL writer replaced the actual retained frame getter.");
        Validate(actor, fields, globals, updates);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var cold = new LateActorTimeFixture(actor, fields, common, updates, globals: globals);
            var before = cold.Capture(); var coldGlobals = RoundTripPackage(cold.Globals.Capture());
            Validate(before, cold.Base.Fields.Capture(), coldGlobals, cold.Updates.Capture());
            Require(before.LastCall!.CalendarDay == actor.LastCall!.CalendarDay &&
                coldGlobals.PlayerFrameCalendar!.LastRead == actor.LastCall.CalendarDay &&
                coldGlobals.PlayerFrameCalendar.Process != actor.LastCall.CalendarDay!.Owner!.Process,
                "Cold calendar replayed the getter or equated its historical read to the newest process.");
            cold.Enter(actor.LastCall.Invocation.Caller.Ordinal + 1);
            actor = cold.Capture(); fields = RoundTripPackage(cold.Base.Fields.Capture());
            common = RoundTripPackage(cold.Base.Common.Capture()); updates = RoundTripPackage(cold.Updates.Capture());
            cold.Globals.Set(cold.Forms.Day, cycle + 3); globals = RoundTripPackage(cold.Globals.Capture());
            Validate(actor, fields, globals, updates);
            var read = actor.LastCall!.CalendarDay!;
            var future = read with { Owner = read.Owner! with { Sequence = globals.PlayerFrameCalendar!.Sequence + 1 } };
            Reject(() => Validate(actor with { LastCall = actor.LastCall with { CalendarDay = future } }, fields, globals, updates));
        }
        using (var repeated = new LateActorTimeFixture())
        {
            for (var ordinal = 1; ordinal <= 32; ordinal++) repeated.Enter(ordinal);
            var retained = RoundTripPackage(repeated.Globals.Capture());
            Require(retained.PlayerFrameCalendar is { Publications.Count: 1, LastRead.Owner.Caller.Ordinal: 32 } &&
                retained.Values.Count == 6,
                "Frame-calendar bookkeeping grew with repeated frames or changed the real GLOBAL domain.");
            Validate(repeated.Capture(), repeated.Base.Fields.Capture(), retained, repeated.Updates.Capture());
        }
        using (var blocked = new LateActorTimeFixture())
        {
            blocked.Enter(); var previous = blocked.Capture();
            blocked.Base.Observe(FalloutReferenceScriptExtraWriter.ReferenceFlags);
            var changed = blocked.Base.Fields.Capture();
            Validate(previous, changed, blocked.Globals.Capture(), blocked.Updates.Capture());
            var unavailable = previous.LastCall!.ReferenceFlags! with { Sequence = changed.ScriptInitialization!.Calls[^1].Changed };
            Reject(() => Validate(previous with { LastCall = previous.LastCall with { ReferenceFlags = unavailable } },
                changed, blocked.Globals.Capture(), blocked.Updates.Capture()));
        }
        var retired = new LateActorTimeFixture(); retired.Enter(); retired.Dispose();
        Reject(() => retired.Globals.Set(retired.Forms.Day, 2));
        Console.WriteLine("OPENNV_LATE_ACTOR_TIME_READ_OWNER_PASS sourceQualifiedFlags=true actualGlobalGetter=true " +
            "historicalReads=true coldCycles=3 boundedPerFrameRetention=true wholePlayer=UNEXECUTED saveQuitColdContinue=UNEXECUTED");

        void Validate(FalloutPlayerLateActorSnapshot value, FalloutPackageCommandFieldSnapshot actualFields,
            FalloutGlobalStateSnapshot actualGlobals, FalloutActorUpdateSnapshot actualUpdates) =>
            FalloutPlayerLateActorTimeSaveContract.Validate(value, actualFields, actualGlobals, actualUpdates, fixture.Forms.Day);
        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    }
}
