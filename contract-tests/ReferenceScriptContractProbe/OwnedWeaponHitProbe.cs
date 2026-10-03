using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedWeaponHitProbe
{
    internal static void Run(string game, string ttwRoot, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", ttwRoot, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        FalloutFormKey[] targets = [new("Fallout3.esm", 0x0304eb), new("Fallout3.esm", 0x076746), new("Fallout3.esm", 0x07674b)];
        var placed = targets.Select(records.GetEffective).ToArray();
        var targetBase = records.GetEffective(new("Fallout3.esm", 0x0304db));
        var sourceScript = records.GetEffective(FalloutDialogueTopic.RequiredForm(targetBase, "SCRI"));
        var quest = records.GetEffective(new("FalloutNV.esm", 0x014e84));
        var questScript = records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
        var tutorial = records.GetEffective(new("FalloutNV.esm", 0x059c85));
        var weapon = records.GetEffective(new("FalloutNV.esm", 0x0c0327));
        if (targetBase.Signature != "ACTI" || sourceScript.Signature != "SCPT" ||
            sourceScript.FormKey != new FalloutFormKey("FalloutNV.esm", 0x0304d8) ||
            quest.Signature != "QUST" || questScript.Signature != "SCPT" || tutorial.Signature != "QUST" || weapon.Signature != "WEAP" ||
            placed.Any(record => record.Signature != "REFR" || FalloutDialogueTopic.RequiredForm(record, "NAME") != targetBase.FormKey))
            throw new InvalidDataException("Selected weapon-hit audit lost its winning targets, source script, quests or weapon.");
        var hashes = placed.Concat([targetBase, sourceScript, quest, questScript, tutorial, weapon])
            .Select(record => (Record: record, Hash: SHA256.HashData(record.ReadData()))).ToArray();
        var source = sourceScript.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
        if (source.Length != 1) throw new InvalidDataException("Selected target script has ambiguous source.");
        var blocks = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(source[0].Data.Span));
        if (blocks.Count != 1 || !blocks[0].Event.Equals("OnHitWith", StringComparison.OrdinalIgnoreCase) || blocks[0].Filter is not null)
            throw new InvalidDataException("Selected target no longer has its original unfiltered weapon-hit block.");
        var bindings = new FalloutScriptBindings(records, placed[0], sourceScript, sourceScript.ReadSubrecords());
        var count = bindings.Variable("CG02.targetCount");
        if (bindings.Form("CG02").FormKey != quest.FormKey || bindings.Form("CGTutorial").FormKey != tutorial.FormKey || count.Owner != quest.FormKey)
            throw new InvalidDataException("Original target lost its compiled master-adjusted quest bindings.");
        var cell = FalloutCellSceneReader.ParentCell(placed[0]) ?? throw new InvalidDataException("Target has no source cell.");
        if (placed.Any(record => FalloutCellSceneReader.ParentCell(record) != cell))
            throw new InvalidDataException("Original practice targets no longer share their source cell.");
        var scene = FalloutCellSceneReader.Read(records, cell);
        var attacker = records.RuntimeFormKey(0x14);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(scene);
        var quests = FixtureQuests();
        var trace = new List<string>();
        var scripts = Scripts(world, quests, trace, playing: false);
        Contact(world, quests, scripts, trace, 0);

        // This is an isolated in-memory hit/script fixture. Its initial running
        // stage50 is supplied, not reached by ordinary campaign input. Only the
        // target script runs; its stage55 request does not execute that stage's
        // radroach/package results, and no live game or user save is modified.
        var savedReferences = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))
            ?? throw new InvalidDataException("Weapon-hit reference fixture did not serialize.");
        var savedQuests = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))
            ?? throw new InvalidDataException("Weapon-hit quest fixture did not serialize.");
        var prefix = trace.ToArray();
        Contact(world, quests, scripts, trace, 1);
        Contact(world, quests, scripts, trace, 2);
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(savedReferences);
        cold.LoadCell(scene);
        var coldQuests = new FalloutQuestState(records);
        coldQuests.Restore(savedQuests);
        var coldTrace = new List<string>(prefix);
        var coldScripts = Scripts(cold, coldQuests, coldTrace, playing: false);
        Contact(cold, coldQuests, coldScripts, coldTrace, 1);
        Contact(cold, coldQuests, coldScripts, coldTrace, 2);
        if (quests.Variable(quest.FormKey, count.Index) != 3 || quests.Stage(quest.FormKey) != 55 ||
            !trace.SequenceEqual(coldTrace) || JsonSerializer.Serialize(quests.Capture()) != JsonSerializer.Serialize(coldQuests.Capture()) ||
            JsonSerializer.Serialize(world.Capture()) != JsonSerializer.Serialize(cold.Capture()))
            throw new InvalidDataException("Consumed hit continuation changed original counts, effect order or cold source state.");

        Branch(running: false, playing: false);
        Branch(running: true, playing: true);
        if (hashes.Any(pair => !pair.Hash.SequenceEqual(SHA256.HashData(pair.Record.ReadData()))))
            throw new InvalidDataException("Weapon-hit fixture changed winning source bytes.");
        Console.WriteLine($"OPENNV_OWNED_WEAPON_HIT_PASS script={sourceScript.FormKey} weapon={weapon.FormKey} targets={targets.Length} " +
            "contactFrames=3 originalCount=3 stage55Request=true sourceOrder=true consumedOnce=true cold=true runningGuard=true animationGuard=true " +
            "masterAdjusted=true sourceReadonly=true fixture=isolated-weapon-hit-script nativeContact=separate campaignProgress=unverified parity=unverified");

        FalloutQuestState FixtureQuests()
        {
            var state = new FalloutQuestState(records);
            state.SetRunning(quest.FormKey, true);
            state.EnterStage(quest.FormKey, 50);
            state.SetVariable(quest.FormKey, count.Index, 0);
            return state;
        }

        string Value(FalloutQuestState state) => state.Variable(quest.FormKey, count.Index).ToString("R", CultureInfo.InvariantCulture);

        FalloutReferenceScripts Scripts(FalloutReferenceWorld owner, FalloutQuestState state, List<string> output, bool playing) =>
            new(records, owner, state, new((_, _) => false, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.SetStage || !targets.Contains(effect.Source) ||
                    !(effect.Target == tutorial.FormKey && effect.Stage == 62 || effect.Target == quest.FormKey && effect.Stage == 55))
                    throw new InvalidDataException("Original target emitted an unexpected isolated effect.");
                output.Add($"stage:{effect.Source}:{effect.Target}:{effect.Stage}:count={Value(state)}");
                state.EnterStage(effect.Target!.Value, effect.Stage);
            }, PlayGroup: (reference, group, initialization) =>
            {
                if (!targets.Contains(reference) || !group.Equals("forward", StringComparison.OrdinalIgnoreCase) || initialization != 1)
                    throw new InvalidDataException("Original target changed its source animation request.");
                output.Add($"forward:{reference}:1:count={Value(state)}");
            }, IsAnimPlaying: (reference, group) =>
            {
                if (!targets.Contains(reference) || group is not null)
                    throw new InvalidDataException("Original target changed its unqualified animation predicate.");
                return playing;
            }));

        void Contact(FalloutReferenceWorld owner, FalloutQuestState state, FalloutReferenceScripts executor, List<string> output, int index)
        {
            var reference = targets[index];
            var before = output.Count;
            var questBefore = JsonSerializer.Serialize(state.Capture());
            owner.HitEvents.Mark(reference, attacker, weapon.FormKey, FalloutReferenceHitKind.Projectile);
            if (output.Count != before || questBefore != JsonSerializer.Serialize(state.Capture()))
                throw new InvalidDataException("Queued contact executed the original target before frame admission.");
            var batch = owner.HitEvents.SnapshotPending(reference);
            if (batch.Events.Count != 2 || batch.Events.Any(item => item.ActionReference is not null) ||
                !batch.Events.Select(item => item.Name).Order().SequenceEqual(new[] { "OnHit", "OnHitWith" }))
                throw new InvalidDataException("Projectile contact lost typed hit admission or acquired an action reference.");
            var results = executor.DispatchFrame(reference, batch.Events, 1.0 / 60);
            if (results.Any(result => result.Error is not null) || results.Sum(result => result.Blocks) != 1 ||
                results.Single(result => result.Event == "OnHitWith").Blocks != 1)
                throw new InvalidDataException("Original target weapon-hit script failed: " + string.Join(';', results.Select(result => result.Error)));
            owner.HitEvents.Consume(batch);
            string[] expected = [
                $"forward:{reference}:1:count={index}",
                $"stage:{reference}:{tutorial.FormKey}:62:count={index + 1}",
                .. index == 2 ? new[] { $"stage:{reference}:{quest.FormKey}:55:count=3" } : []];
            if (!output.Skip(before).SequenceEqual(expected) || state.Variable(quest.FormKey, count.Index) != index + 1 ||
                state.Stage(quest.FormKey) != (index == 2 ? 55 : 50))
                throw new InvalidDataException("Original target lost animation-before-count, tutorial-before-stage or its three-hit guard.");
            var empty = owner.HitEvents.SnapshotPending(reference);
            if (empty.Events.Count != 0 || executor.DispatchFrame(reference, empty.Events, 0).Count != 0 || output.Count != before + expected.Length)
                throw new InvalidDataException("A consumed target hit executed again without a fresh contact.");
            owner.HitEvents.Consume(empty);
        }

        void Branch(bool running, bool playing)
        {
            using var owner = new FalloutReferenceWorld(records);
            owner.LoadCell(scene);
            var state = FixtureQuests();
            state.SetRunning(quest.FormKey, running);
            var output = new List<string>();
            var executor = Scripts(owner, state, output, playing);
            owner.HitEvents.Mark(targets[0], attacker, weapon.FormKey, FalloutReferenceHitKind.Projectile);
            var batch = owner.HitEvents.SnapshotPending(targets[0]);
            if (executor.DispatchFrame(targets[0], batch.Events, 1.0 / 60).Any(result => result.Error is not null))
                throw new InvalidDataException("Original target running/animation guard failed.");
            owner.HitEvents.Consume(batch);
            var expected = running ? new[] { $"stage:{targets[0]}:{tutorial.FormKey}:62:count=1" } :
                [$"forward:{targets[0]}:1:count=0"];
            if (!output.SequenceEqual(expected) || state.Variable(quest.FormKey, count.Index) != (running ? 1 : 0) || state.Stage(quest.FormKey) != 50)
                throw new InvalidDataException("Original target ignored its running or already-playing source guard.");
        }
    }
}
