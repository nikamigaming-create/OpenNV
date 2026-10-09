using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class PlayerAbilityScriptContracts
{
    private static readonly ConditionalWeakTable<FalloutPlayerAbilityScripts, AbilityClockFixture> AbilityClocks = new();

    // This is an authored clock/source-contract fixture, not a selected-engine
    // or original-DLL execution proof. It uses the product clock and actual
    // entered source-frame observation; there is no product test hook.
    private sealed class AbilityClockFixture
    {
        private bool _entered;
        private ulong _frame;
        internal FalloutRestWorldTime World { get; }
        internal FalloutScriptedEffectClock Clock { get; }
        internal AbilityClockFixture(FalloutPluginStack records, FalloutScriptedEffectClockSnapshot? restore)
        {
            var declaration = new FalloutSleepWaitSource(
                "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
                new('1', 64), new('2', 64), FalloutSleepWaitSource.CurrentContractSha256, true, true, 24);
            var source = FalloutRestWorldTimeSource.Read(declaration);
            FalloutAdvancementActivityObservation Observe() => new(_entered ?
                FalloutAdvancementActivityState.Satisfied : FalloutAdvancementActivityState.Held, "authored-current-effect-frame");
            World = new(declaration, Observe, restore?.WorldPrefix ?? new(FalloutRestWorldTime.Schema, source.Identity, 0, 0, null));
            Clock = new(World, declaration, records.NumericSettings, Observe, restore);
            _frame = restore?.WorldPrefix.Last?.Frame ?? 0;
        }
        internal void Advance(FalloutPlayerAbilityScripts owner, float seconds)
        {
            _entered = true;
            try { World.AdvanceActualSourceFrame(checked(++_frame), seconds); owner.AdvanceFromCurrentSourceFrame(); }
            finally { _entered = false; }
        }
    }

    private static void BindAbilityClock(FalloutPluginStack records, FalloutPlayerAbilityScripts owner,
        FalloutScriptedEffectClockSnapshot? restore = null)
    {
        var clock = new AbilityClockFixture(records, restore); owner.BindClock(clock.Clock); AbilityClocks.Add(owner, clock);
    }

    private static byte[] AbilityLifecycleSettings() => Join(
        Setting(0x600, "fActiveEffectConditionUpdateInterval", 1),
        Setting(0x601, "fAVDHealthEnduranceOffset", 5), Setting(0x602, "fAVDHealthEnduranceMult", 20),
        Setting(0x603, "fAVDHealthLevelMult", 5), Setting(0x604, "fAVDActionPointsBase", 50), Setting(0x605, "fAVDActionPointsMult", 10),
        Record("GMST", 0x606, Field("EDID", Text("iXPBase")), Field("DATA", BitConverter.GetBytes(200))),
        Record("GMST", 0x607, Field("EDID", Text("iXPBumpBase")), Field("DATA", BitConverter.GetBytes(50))));

    private static void CheckCompiledEffectLifecycle(string directory)
    {
        var code = Join(AbilityInstruction(0x1d),
            AbilityBlock(17, Join(AbilitySet('s', 1, Encoding.ASCII.GetBytes(" 4")), AbilityActorValue(7, 3, 1))),
            AbilityBlock(19, Join(AbilitySet('s', 1, Encoding.ASCII.GetBytes(" 8")), AbilityActorValue(7, 2, 1))),
            AbilityBlock(18, Join(AbilitySet('s', 1, Encoding.ASCII.GetBytes(" 11")), AbilityActorValue(7, 5, 1))));
        File.WriteAllBytes(Path.Combine(directory, Plugin), Join(RewriteAbilityScript(Fixture(false), fields => ReplaceAbilityCode(fields, code, 2, 1)), AbilityLifecycleSettings()));
        var unchanged = SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, Plugin)));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
        var selected = true;
        var effects = new FalloutPlayerAbilityScripts(records, () => selected ? [Key(0x21)] : [], _ => throw new InvalidOperationException("Unconditional fixture cannot invoke a condition."));
        var executor = AbilityLifecycleExecutor(records, world, actor, skills);
        BindAbilityClock(records, effects); effects.BindExecutor(executor.ExecuteActiveEffect);
        effects.BindTargetVitals(FalloutPlayerVitals.PrepareFromActorValues(records, actor));
        effects.Synchronize();
        Require(BitConverter.UInt64BitsToDouble(effects.Capture().Effects.Single().Locals.Single().Payload) == 4,
            "Start did not use its genuine initial raw local cells.");
        AbilityClocks.GetValue(effects, _ => throw new InvalidOperationException()).Advance(effects, .25f);
        AbilityClocks.GetValue(effects, _ => throw new InvalidOperationException()).Advance(effects, .75f);
        var saved = JsonSerializer.Deserialize<FalloutPlayerAbilityScriptsSnapshot>(JsonSerializer.Serialize(effects.Capture()))!;
        var entry = saved.Effects.Single();
        Require(entry.Timeline!.ElapsedBits == BitConverter.SingleToUInt32Bits(1) &&
            entry.Compiled!.Events.Single(row => row.Event == 19).Cycle == 2 &&
            entry.Compiled.Events.Single(row => row.Event == 18).Cycle == 0 &&
            actor.Capture().Values[7].Permanent == 7,
            "Repeated Update lost actual time, reset cells, reused a completed prefix or ran Finish prematurely.");
        var cold = new FalloutPlayerAbilityScripts(records, () => selected ? [Key(0x21)] : [],
            _ => throw new InvalidOperationException("Unconditional cold fixture cannot invoke a condition."), saved);
        BindAbilityClock(records, cold, saved.Clock); cold.BindExecutor(executor.ExecuteActiveEffect);
        cold.BindTargetVitals(FalloutPlayerVitals.PrepareFromActorValues(records, actor));
        cold.Synchronize();
        Require(actor.Capture().Values[7].Permanent == 7, "Cold Start replayed its committed mutation.");
        AbilityClocks.GetValue(cold, _ => throw new InvalidOperationException()).Advance(cold, .5f);
        selected = false; cold.Synchronize();
        var finished = cold.Capture().Effects.Single();
        Require(finished.Timeline!.FinishApplied && finished.Timeline.Removed &&
            finished.Compiled!.Lifetime!.Finished && finished.Compiled.Events.Single(row => row.Event == 18).Cycle == 1 &&
            BitConverter.UInt64BitsToDouble(finished.Locals.Single().Payload) == 11 &&
            actor.Capture().Values[7].Permanent == 14 && world.InstanceCount == 0,
            "Actual removal failed to Finish/retire the same source cells or fabricated a target actor.");
        cold.Synchronize();
        Require(actor.Capture().Values[7].Permanent == 14, "A retired Finish replayed its source suffix.");
        Require(unchanged.SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, Plugin)))), "Lifecycle altered its original authored inputs.");
        Require(FalloutScriptedActiveEffect.CrossesConditionBucket(.75f, .25f, 1) &&
            !FalloutScriptedActiveEffect.CrossesConditionBucket(.25f, .25f, 1), "Condition interval used query frequency or a fixed fallback.");
        CheckCompiledEffectUpdateFailure(directory);
        Console.WriteLine("OPENNV_COMPILED_EFFECT_LIFECYCLE_PASS lane=authored_source actualClockLease=true startUpdateFinish=true sameCells=true coldPrefix=true nativeEffectProjection=UNOWNED restTraversal=UNOWNED parity=UNVERIFIED");
    }

    private static FalloutReferenceScripts AbilityLifecycleExecutor(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutPlayerActorValues actor, FalloutPlayerSkills skills) => new(records, world, new FalloutQuestState(records),
        new((_, _) => throw new NotSupportedException("Fixture furniture is unowned."),
            _ => throw new NotSupportedException("Fixture presentation is unowned."),
            ReadActorValue: (_, name, kind) => skills.IsSkill(name) ? skills.ReadSkill(name, kind) : actor.Read(FalloutPlayerActorValues.SpecialValue(name), kind),
            ChangeActorValue: (target, name, operation, value) =>
            {
                Require(records.RuntimeFormId(target) == 0x14, "Lifecycle used another actual actor.");
                if (skills.IsSkill(name)) skills.ChangeSkill(name, operation, value); else actor.Change(name, operation, value);
            }));

    private static void CheckCompiledEffectUpdateFailure(string directory)
    {
        var code = Join(AbilityInstruction(0x1d), AbilityBlock(17, AbilityActorValue(7, 3, 1)),
            AbilityBlock(19, Join(AbilityActorValue(7, 2, 1), AbilityActorValue(0xffff, 1, 1))));
        File.WriteAllBytes(Path.Combine(directory, Plugin), Join(RewriteAbilityScript(Fixture(false), fields => ReplaceAbilityCode(fields, code, 2, 1)), AbilityLifecycleSettings()));
        using var records = FalloutPluginStack.Load(directory, [Plugin]);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutPlayerActorValues(records); var skills = Skills(records, actor);
        var effects = new FalloutPlayerAbilityScripts(records, () => [Key(0x21)], _ => throw new InvalidOperationException());
        BindAbilityClock(records, effects); effects.BindTargetVitals(FalloutPlayerVitals.PrepareFromActorValues(records, actor));
        effects.BindExecutor(AbilityLifecycleExecutor(records, world, actor, skills).ExecuteActiveEffect); effects.Synchronize();
        Reject(() => AbilityClocks.GetValue(effects, _ => throw new InvalidOperationException()).Advance(effects, .5f));
        var saved = JsonSerializer.Deserialize<FalloutPlayerAbilityScriptsSnapshot>(JsonSerializer.Serialize(effects.Capture()))!;
        var entry = saved.Effects.Single(); var update = entry.Compiled!.Events.Single(row => row.Event == 19);
        Require(entry.Started && entry.Error is not null && entry.Timeline!.ElapsedBits == BitConverter.SingleToUInt32Bits(.5f) &&
            update.Cursor.CommittedInstructions > 0 && update.Receipt?.Disposition == "closed-failure" && saved.Clock!.Failure is not null &&
            actor.Capture().Values[7].Permanent == 5, "Failed Update lost elapsed or its genuinely retired mutation prefix.");
        var cold = new FalloutPlayerAbilityScripts(records, () => [Key(0x21)], _ => throw new InvalidOperationException(), saved);
        BindAbilityClock(records, cold, saved.Clock); cold.BindExecutor(_ => throw new InvalidOperationException("Failed Update cannot restart."));
        Reject(cold.Synchronize);
        Require(actor.Capture().Values[7].Permanent == 5, "Failed cold Update reran its committed prefix.");
        Reject(() => FalloutPlayerAbilityScripts.Validate(saved with { Clock = null }));
        Reject(() => FalloutPlayerAbilityScripts.Validate(saved with { Effects = [entry with { Timeline = null }] }));
    }
}
