using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ChallengeContracts
{
    private static void SharedInterpreterContexts(string directory)
    {
        var nested = Path.Combine(directory, "shared-interpreter-nested"); Directory.CreateDirectory(nested);
        // The source completion runs the actual shared compiled executor. Its
        // statistic command enters another real CHAL completion synchronously.
        Write(nested, Challenge(0x200, 11, 1, 0, 2, script: 0x501),
            Challenge(0x201, 11, 1, 0, 1, script: 0x500),
            Script(0x500, Program(0, Join(Set(7), ModStatistic(2, 1), Set(9)))),
            Script(0x501, Program(0, Set(3))));
        using (var records = Load(nested))
        using (var warm = new Fixture(records))
        {
            warm.Statistics.Mod(1, 1, "authored-real-shared-interpreter-nesting");
            var contexts = Copy(warm.World.CaptureCampaignScriptContexts());
            var call = contexts.LastCall!;
            Require(contexts.Calls == 2 && contexts.Contexts == 2 && contexts.CachedContext == 1 &&
                call is { Cached: true, Context: 1, Disposition: "returned", Seconds: 0,
                    Children: [{ Cached: false, Context: 2, Disposition: "returned", Seconds: 0 }] } &&
                call.Program == Key(0x500) && call.Children.Single().Program == Key(0x501) &&
                call.Invocations.Count == 1 && call.Children.Single().Invocations.Count == 1 &&
                call.Invocations.Single().Invocation != call.Children.Single().Invocations.Single().Invocation &&
                warm.Challenges.State(Key(0x200)).Completed && warm.Challenges.State(Key(0x201)).Completed &&
                warm.Statistics.Read(27) == 2 && warm.World.ScriptManualSaves.EnteredInvocations == 0,
                "Nested immediate calls allocated another outer cache, shared fresh locals, lost actual leases or fabricated a return.");
            using var cold = new FalloutScriptEngineContexts(FalloutImmediateScriptSource.Read(warm.Challenges.Source!), contexts);
            cold.RequireSources(records);
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(contexts),
                "Cold shared interpreter entered a call or changed the returned nested lifetime.");
        }
        var failed = Path.Combine(directory, "shared-interpreter-callback-failure"); Directory.CreateDirectory(failed);
        Write(failed, Challenge(0x200, 11, 1, 0, 1, script: 0x500),
            Script(0x500, Program(0, Join(Set(7), ModStatistic(2, 1), Set(9)))));
        using (var records = Load(failed))
        using (var warm = new Fixture(records))
        {
            warm.DuringMenu = _ => throw new IOException("Authored committed source callback failure.");
            Reject(() => warm.Statistics.Mod(1, 1, "authored-context-retained-prefix"));
            var contexts = Copy(warm.World.CaptureCampaignScriptContexts());
            var reward = warm.Challenges.Capture().LastDispatch!.Attempts.Single().Reward!;
            Require(contexts.LastCall is { Disposition: "closed-failure", Error: not null, Invocations.Count: 1 } &&
                warm.Statistics.Read(2) == 1 && reward.Events.Single().Cursor.CommittedInstructions == 1 &&
                reward.Events.Single().Receipt is { Disposition: "closed-failure", Invocation: > 0 },
                "A throwing actual callback discarded its changed counter, committed VM prefix or failed source context.");
            using var cold = new FalloutScriptEngineContexts(FalloutImmediateScriptSource.Read(warm.Challenges.Source!), contexts);
            cold.RequireSources(records);
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(contexts),
                "Cold context converted a genuine callback fault into an unattempted or successful call.");
        }
        Console.WriteLine("OPENNV_CHALLENGE_INTERPRETER_CONTEXT_PASS actualSharedExecution=true cachedOuter=true freshNested=true actualLeases=true callbackCommittedPrefix=true coldNoExecution=true nativeGameplay=UNEXECUTED");
    }

    private static void MutableBucketContexts(string directory)
    {
        var first = Path.Combine(directory, "nested-stable-head"); Directory.CreateDirectory(first);
        // Registration is prepended. The actual parent holds the stable head;
        // a completed-counter child retires its interior and rebuilds globally.
        Write(first, Challenge(0x200, 11, 1, 0, 27), Challenge(0x201, 11, 1, 0, 1));
        using (var records = Load(first))
        using (var warm = new Fixture(records))
        {
            Require(warm.Challenges.Capture().Buckets[11].Members.SequenceEqual([Key(0x201), Key(0x200)]),
                "Original bucket prepend became registration order.");
            warm.Statistics.Mod(1, 1, "authored-stable-head-nested-rebuild");
            var state = Copy(warm.Challenges.Capture()); var counters = Copy(warm.Statistics.Capture());
            Require(state.LastDispatch is { Complete: true, RebuildEntered: true, Children: [{ Complete: true, RebuildEntered: true }] } &&
                state.BucketGeneration == 3 && state.Buckets[11].Members.Count == 0 && state.Buckets[11].Excluded!.Count == 2 &&
                state.Entries.All(row => row.Completed) && warm.Statistics.Read(27) == 1 &&
                state.LastDispatch.Traversal.Any(row => row.StableHead && row.Operation == "next" && row.ObservedGeneration == 2),
                "Valid nested rebuild refused a surviving head, froze its old Next, or invented another counter.");
            using var cold = new Fixture(records, statistics: counters, challenges: state);
            Require(JsonSerializer.Serialize(cold.Challenges.Capture()) == JsonSerializer.Serialize(state) && cold.MenuCalls == 0,
                "Cold mutable bucket continuation rebuilt or replayed a completed source effect.");
        }
        var other = Path.Combine(directory, "nested-retired-interior"); Directory.CreateDirectory(other);
        Write(other, Challenge(0x200, 11, 1, 0, 1), Challenge(0x201, 11, 1, 0, 27), Challenge(0x202, 11, 100, 0, 1));
        using (var records = Load(other))
        using (var warm = new Fixture(records))
        {
            Reject(() => warm.Statistics.Mod(1, 1, "authored-interior-retired-after-real-child"));
            var saved = Copy(warm.Challenges.Capture()); var counters = Copy(warm.Statistics.Capture());
            Require(saved.LastDispatch is { Complete: false, Children: [{ Complete: true, RebuildEntered: true }] } &&
                saved.LastDispatch.Error!.Contains("cursor-interior-node-retired", StringComparison.Ordinal) &&
                saved.LastDispatch.Traversal.Last() is { StableHead: false, Operation: "next-refusal", Error: not null } &&
                warm.Challenges.State(Key(0x200)).Completed && warm.Challenges.State(Key(0x201)).Completed &&
                warm.Challenges.State(Key(0x202)).Progress == 1 && warm.Statistics.Read(27) == 1 && saved.BucketGeneration == 2,
                "Retired node silently continued, deferred the actual child rebuild or discarded committed flags/counters.");
            using var cold = new Fixture(records, statistics: counters, challenges: saved);
            Reject(() => cold.Statistics.Mod(1, 1, "authored-retired-cold-no-replay"));
            Require(cold.MenuCalls == 0 && JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(cold.Challenges.Capture()),
                "Cold retired-cursor failure replayed its committed prefix.");
        }
        var unlock = Path.Combine(directory, "linked-unlock"); Directory.CreateDirectory(unlock);
        Write(unlock, Challenge(0x200, 11, 100, 0, 1), Challenge(0x201, 11, 100, 1, 1));
        using (var records = Load(unlock))
        using (var warm = new Fixture(records))
        {
            var initial = warm.Challenges.Capture();
            Require(initial.Buckets[11].Members.SequenceEqual([Key(0x200)]) &&
                initial.Buckets[11].Excluded!.SequenceEqual([Key(0x201)]), "Source disabled member was admitted to the active bucket.");
            warm.Challenges.Unlock(Key(0x201)); warm.Challenges.Unlock(Key(0x201));
            var actual = warm.Challenges.Capture();
            Require(actual.BucketGeneration == initial.BucketGeneration && actual.Buckets[11].Members.SequenceEqual([Key(0x201), Key(0x200)]) &&
                actual.Buckets[11].Excluded!.Count == 0, "Unlock rebuilt the whole registry, duplicated a node or lost its prepend order.");
        }
        Console.WriteLine("OPENNV_CHALLENGE_LINKED_CONTEXT_PASS stableHeadNested=true retiredInteriorPrefix=true reversePrepend=true unlockMovement=true coldNoReplay=true nativeGameplay=UNEXECUTED");
    }

    private static void MainScriptScalarContexts()
    {
        var source = new FalloutImmediateScriptSource(Engine, new('1', 64),
            FalloutImmediateScriptSource.Read(new FalloutAdvancementRuntimeReceipt(Engine, new('a', 64), new('b', 64), new('c', 64),
                new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
                new(1, 10, FalloutPermanentIntelligenceInteger.Floor))).ContractSha256);
        var declaration = FalloutActorProcessRuntimeDeclaration.ForExecutable(Engine);
        FalloutCombatActorIdentity Identity(FalloutFormKey key) => new(key, Key(7), "ENGINE_PLAYER", 0,
            Engine, "NPC_", 0, new('4', 64), true);
        using var owner = new FalloutActorProcessRuntimeState(declaration, "authored-current-Main", Key(0x14), Identity);
        owner.ConstructScriptFrame(source, null);
        var initial = owner.Observe();
        Require(initial.Allows && owner.CaptureScriptFrame() is { Frame: 0, LastSite: null, LastQuery: null },
            "Loader-zero field fabricated an actual Main frame/sample.");
        var rows = new[] { new FalloutInterfaceFadeCatalogRow(0, FalloutInterfaceFadeRoot.Primary, "textures/interface/faders/a.dds"),
            new FalloutInterfaceFadeCatalogRow(1, FalloutInterfaceFadeRoot.Secondary, "textures/interface/faders/a.dds"),
            new FalloutInterfaceFadeCatalogRow(2, FalloutInterfaceFadeRoot.Secondary, "textures/interface/faders/b.dds") };
        var fadeSource = new FalloutInterfaceFadeSource(new(Engine, FalloutInterfaceFadeArithmetic.WideQuotientThenFloat32, rows),
            source.RuntimeSha256, rows.Select(row => new FalloutInterfaceFadeTexture(row.Channel, row.TexturePath, new('f', 64))).ToArray(),
            FalloutInterfaceFadeSource.CurrentContractSha256);
        var fade = new FalloutInterfaceFade(fadeSource); var published = 0;
        fade.BindNative(new(_ => ++published, _ => { }, _ => --published));
        using var lease = owner.BindScriptFrameFade(fade);
        Reject(() => owner.SampleScriptFrame(1, FalloutMainScriptSampleSite.AfterMainChildren));
        owner.SampleScriptFrame(1, FalloutMainScriptSampleSite.BeforeMainChildren);
        Reject(() => owner.CaptureScriptFrame());
        Reject(() => owner.SampleScriptFrame(2, FalloutMainScriptSampleSite.BeforeMainChildren));
        fade.Start(1, 0, false);
        Require(published == 1 && fade.QueryIncreasingOpaque(1).Value && owner.Observe().Allows,
            "Immediate event substituted a live fade query for the separately cached Main byte.");
        owner.SampleScriptFrame(1, FalloutMainScriptSampleSite.AfterMainChildren);
        Require(!owner.Observe().Allows, "Source opaque increasing channel1 did not produce the independent cached blocker.");
        Reject(() => owner.RequireCurrent(initial));
        fade.End(1, false, () => new(false, "authored-global-force-not-set"));
        Require(!fade.QueryIncreasingOpaque(1).Value && !owner.Observe().Allows,
            "Downward fade guessed an early cached Main clear.");
        owner.SampleScriptFrame(2, FalloutMainScriptSampleSite.BeforeMainChildren);
        owner.SampleScriptFrame(2, FalloutMainScriptSampleSite.AfterMainChildren);
        Require(owner.Observe().Allows, "Actual next Main sample did not consume the source direction/scalar.");
        var state = owner.CaptureScriptFrame(); var runtime = owner.Capture();
        using var cold = new FalloutActorProcessRuntimeState(declaration, "authored-current-Main", Key(0x14), Identity, runtime);
        cold.ConstructScriptFrame(source, state);
        Require(cold.CaptureScriptFrame() == state && cold.Observe() == owner.Observe(),
            "Cold Main scalar invented another sample, field write or frame.");
        var changedFadeSource = fadeSource with
        { Textures = fadeSource.Textures.Select(row => row with { BytesSha256 = new('e', 64) }).ToArray() };
        Reject(() => cold.BindScriptFrameFade(new FalloutInterfaceFade(changedFadeSource)));
        var coldFade = new FalloutInterfaceFade(fadeSource, fade.Capture());
        Reject(() => coldFade.QueryIncreasingOpaque(1));
        using var unbound = new FalloutActorProcessRuntimeState(declaration, "authored-unbound-Main", Key(0x14), Identity);
        unbound.ConstructScriptFrame(source, null);
        Reject(() => unbound.SampleScriptFrame(1, FalloutMainScriptSampleSite.BeforeMainChildren));
        Require(unbound.CaptureScriptFrame() is { Frame: 1, FailureType: not null, Error: not null },
            "Missing actual channel producer cleared the genuine attempted Main write.");
        Reject(() => unbound.Observe());
        Console.WriteLine("OPENNV_CHALLENGE_MAIN_SCALAR_PASS sourceCtorOnly=true orderedTwoSamples=true menuIndependent=true exactOpaqueQuery=true cachedNotLiveQuery=true missingProducerPrefix=true coldNoWrite=true nativeGameplay=UNEXECUTED");
    }
}
