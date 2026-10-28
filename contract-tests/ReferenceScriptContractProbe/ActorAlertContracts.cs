using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ActorAlertContracts
{
    internal static void RunStoppedCreature(FalloutPluginStack records, FalloutReferenceSnapshot snapshot)
    {
        using var world = new FalloutReferenceWorld(records);
        world.Restore([snapshot]);
        var actor = world.Get(snapshot.Reference);
        var original = actor.SandboxAdmissionFailure!;
        var eventsDispatched = 0;
        foreach (var alerted in new[] { true, false })
        {
            var prior = actor.SandboxAdmissionFailure!;
            world.SetActorAlert(actor.Reference, alerted ? 2 : -1);
            var updated = actor.SandboxAdmissionFailure!;
            var expectedActivity = prior.Activity with { Alerted = alerted, Revision = prior.Activity.Revision + 1 };
            ScriptManualSaveContracts.Require(world.ActorAlerted(actor.Reference) == alerted &&
                updated.Activity == expectedActivity && updated.ActivityRevision == expectedActivity.Revision &&
                updated.SelectionAttempts == original.SelectionAttempts && updated.AiRandomState == original.AiRandomState &&
                updated.EventRevision == original.EventRevision && updated.LastEvent == original.LastEvent &&
                updated.Error == original.Error && actor.Animation.ElapsedSeconds == snapshot.Animation!.ElapsedSeconds,
                "Stopped creature alert lost its shared activity revision, source failure or retained package state.");
            var alertRevision = world.ActorAlertRevision(actor.Reference);
            world.SetActorAlert(actor.Reference, alerted ? 1 : 0);
            ScriptManualSaveContracts.Require(ReferenceEquals(actor.SandboxAdmissionFailure, updated) &&
                world.ActorAlertRevision(actor.Reference) == alertRevision,
                "Repeated stopped creature alert changed its retained receipt.");

            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            cold.RestoreActorOverrides(JsonSerializer.Deserialize<FalloutActorOverrides[]>(
                JsonSerializer.Serialize(world.CaptureActorOverrides()))!);
            var restored = cold.Get(actor.Reference);
            var activity = new FalloutActorActivityState();
            cold.BindActorAlert(actor.Reference, activity);
            restored.SandboxAdmissionFailure!.RestoreActivity(records, restored, activity);
            var events = new FalloutPackageEvents((_, _) => eventsDispatched++);
            restored.SandboxAdmissionFailure.RestoreEvents(records, restored, events);
            ScriptManualSaveContracts.Require(activity.Capture() == updated.Activity &&
                cold.ActorAlerted(actor.Reference) == alerted && events.Revision == original.EventRevision &&
                events.LastEvent == original.LastEvent && eventsDispatched == 0,
                "Cold stopped creature alert reset activity or replayed a retained package event.");

            var invalid = updated with { ActivityRevision = updated.ActivityRevision + 1 };
            actor.SandboxAdmissionFailure = invalid;
            ScriptManualSaveContracts.Reject(() => world.SetActorAlert(actor.Reference, alerted ? 0 : 1));
            ScriptManualSaveContracts.Require(world.ActorAlerted(actor.Reference) == alerted &&
                world.ActorAlertRevision(actor.Reference) == alertRevision &&
                ReferenceEquals(actor.SandboxAdmissionFailure, invalid),
                "Invalid stopped creature alert partially committed its flag, revision or receipt.");
            actor.SandboxAdmissionFailure = updated;
        }
        Console.WriteLine("OPENNV_STOPPED_CREATURE_ALERT_PASS signedInteger=true activityRevision=true unchangedNoOp=true " +
            "sourceFailureRetained=true randomAndClockRetained=true cold=true noPackageEventReplay=true invalidAtomic=true");
    }

    internal static void Run()
    {
        using var fixture = new ScriptSaveFixture("set saved to 1\nOther.SetAlert 2\nset suffix to 7");
        var actor = new FalloutFormKey("Saves.esm", 0x91);
        var activity = new FalloutActorActivityState();
        fixture.World.BindActorAlert(actor, activity);
        activity.SetCombat(true); activity.SetWeaponDrawn(true);
        var revision = activity.Revision;
        ScriptManualSaveContracts.Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null &&
            fixture.World.ActorAlerted(actor) && activity.Alerted && activity.Revision > revision &&
            activity.InCombat && activity.WeaponDrawn && fixture.Value(1) == 1 && fixture.Value(2) == 7,
            "Source SetAlert lost its actual actor flag or changed combat/weapon state.");
        fixture.World.SetActorAlert(actor, 0);
        ScriptManualSaveContracts.Require(!activity.Alerted && activity.InCombat && activity.WeaponDrawn,
            "SetAlert zero cleared independent combat/weapon state.");
        fixture.World.SetActorAlert(actor, -1);
        ScriptManualSaveContracts.Require(!activity.Alerted, "Negative source integer SetAlert became true.");
        fixture.World.UnloadCell(fixture.Cell);
        fixture.World.SetActorAlert(actor, 1);
        var overrides = JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(fixture.World.CaptureActorOverrides()))!;
        using var cold = new FalloutReferenceWorld(fixture.Records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(fixture.World.Capture()))!);
        cold.RestoreActorOverrides(overrides);
        var rebound = new FalloutActorActivityState(); cold.BindActorAlert(actor, rebound);
        ScriptManualSaveContracts.Require(rebound.Alerted && !rebound.InCombat && !rebound.WeaponDrawn,
            "Cold alert state disappeared or invented independent physical/combat continuation.");
        var drifted = overrides.Select(value => value with { SourceSha256 = new string('0', 64) }).ToArray();
        ScriptManualSaveContracts.Reject(() => cold.RestoreActorOverrides(drifted));
        ScriptManualSaveContracts.Require(cold.ActorAlerted(actor), "Failed source validation partially cleared alert state.");
        ScriptManualSaveContracts.Reject(() => cold.RestoreActorOverrides([overrides[0], overrides[0]]));
        var count = cold.CaptureActorOverrides().Count;
        cold.SetActorAlert(fixture.Caller, 1);
        ScriptManualSaveContracts.Require(cold.CaptureActorOverrides().Count == count,
            "Non-actor command cast created an actor flag.");
        ScriptManualSaveContracts.Reject(() => cold.SetActorAlert(new("Saves.esm", 2), 1));
        ScriptManualSaveContracts.Reject(() => cold.SetActorAlert(actor, .5));
        var previousInstances = cold.InstanceCount;
        cold.SetActorAlert(fixture.Player, 1);
        var playerActivity = new FalloutActorActivityState(); cold.BindActorAlert(fixture.Player, playerActivity);
        ScriptManualSaveContracts.Require(playerActivity.Alerted && cold.InstanceCount == previousInstances,
            "Player alert state manufactured a placed world entity.");
        using var playerCold = new FalloutReferenceWorld(fixture.Records);
        playerCold.RestoreActorOverrides(JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(cold.CaptureActorOverrides()))!);
        ScriptManualSaveContracts.Require(playerCold.ActorAlerted(fixture.Player), "Cold player alert lost its owned NPC source binding.");
        foreach (var shared in new[] { false, true })
        {
            using var questFixture = new ScriptSaveFixture("", "Other.SetAlert 1\nset suffix to 5");
            var quests = new FalloutQuestState(questFixture.Records);
            var scripts = new FalloutQuestScripts(questFixture.Records, quests, new HashSet<FalloutFormKey>(), new(),
                references: questFixture.World, defaultProcessingDelay: 0);
            var executor = new FalloutReferenceScripts(questFixture.Records, questFixture.World, quests, new((_, _) => false, _ => { }));
            scripts.Host = new((_, _) => throw new InvalidDataException("Unexpected stage"), _ => 0,
                shared ? executor.ExecuteProgram : null);
            scripts.Advance(0);
            ScriptManualSaveContracts.Require(questFixture.World.ActorAlerted(actor) && quests.Variable(questFixture.Quest, 2) == 5,
                "Shared/fallback quest SetAlert did not use the same actual actor flag.");
        }
        foreach (var shared in new[] { false, true })
        {
            using var questFixture = new ScriptSaveFixture("", "Other.StartCombat player\nset suffix to 5");
            var quests = new FalloutQuestState(questFixture.Records);
            var scripts = new FalloutQuestScripts(questFixture.Records, quests, new HashSet<FalloutFormKey>(), new(),
                references: questFixture.World, defaultProcessingDelay: 0);
            var executor = new FalloutReferenceScripts(questFixture.Records, questFixture.World, quests, new((_, _) => false, _ => { }));
            scripts.Host = new((_, _) => throw new InvalidDataException("Unexpected stage"), _ => 0,
                shared ? executor.ExecuteProgram : null);
            scripts.Advance(0);
            ScriptManualSaveContracts.Require(questFixture.World.Get(actor).Engagement?.Target == questFixture.Player &&
                quests.Variable(questFixture.Quest, 2) == 5,
                "Shared/fallback quest StartCombat lost its compiled actor, target or following statement.");
            using var coldCombat = new FalloutReferenceWorld(questFixture.Records);
            coldCombat.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(questFixture.World.Capture()))!);
            ScriptManualSaveContracts.Require(coldCombat.Get(actor).Engagement?.Target == questFixture.Player,
                "Quest scripted combat lost its target after cold restoration.");
        }
        using (var stopped = new ScriptSaveFixture("set saved to 1\nOther.SetAlert 1\nUnownedSuffix\nset suffix to 9"))
        {
            var result = stopped.Scripts.Activate(stopped.Caller, stopped.Player);
            ScriptManualSaveContracts.Require(result.Error is not null && stopped.Value(1) == 1 && stopped.Value(2) == 0 &&
                stopped.World.ActorAlerted(actor), "Stopped source suffix lost its consumed alert/prefix.");
            using var faultCold = new FalloutReferenceWorld(stopped.Records);
            faultCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(stopped.World.Capture()))!);
            faultCold.RestoreActorOverrides(JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(stopped.World.CaptureActorOverrides()))!);
            faultCold.LoadCell(FalloutCellSceneReader.Read(stopped.Records, stopped.Cell));
            var executor = new FalloutReferenceScripts(stopped.Records, faultCold, new(stopped.Records), new((_, _) => false, _ => { }));
            ScriptManualSaveContracts.Require(executor.Dispatch(stopped.Caller, "GameMode").Error == result.Error &&
                faultCold.ActorAlerted(actor) && faultCold.Get(stopped.Caller).Read(1) == 1,
                "Cold source alert/fault replayed its consumed prefix.");
        }
        Console.WriteLine("OPENNV_ACTOR_ALERT_PASS sourceActor=true signedInteger=true combatAndWeaponIndependent=true " +
            "sharedNativeQueryState=true retainedAndCold=true playerWithoutProxy=true sourceDriftAtomicRefusal=true " +
            "sharedAndFallback=true stoppedPrefixCold=true laterWeaponActivity=unverified");
    }
}
