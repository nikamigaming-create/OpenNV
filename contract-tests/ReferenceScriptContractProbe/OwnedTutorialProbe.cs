using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedTutorialProbe
{
    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var globals = FalloutGlobalState.Read(records);
        var inventory = new FalloutPlayerInventory();
        var messages = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), inventory, globals, references: world);
        var owner = FalloutDialogueTopic.Find(records, "QUST", "CGTutorial");
        var script = FalloutScriptLocals.AttachedScript(records, owner)!;
        var source = script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data;
        var program = FalloutGameModeProgram.Read(source.Span);
        var actorValues = new FalloutPlayerActorValues(records);
        var playerSource = actorValues.Source.Player;
        var race = FalloutDialogueTopic.RequiredForm(records.GetEffective(playerSource), "RNAM");
        var skills = new FalloutPlayerSkills(records, () => actorValues.BaseSpecial, _ => false, () => [], globals, inventory,
            playerSource, () => race, () => false, actorValues: actorValues);
        actorValues.BindConstantModifiers(skills.Modifiers);
        var vitals = FalloutPlayerVitals.FromActorValues(records, actorValues);
        var body = FalloutBodyPartData.Read(records.GetEffective(records.RuntimeFormKey(0x1d)));
        var statistics = FalloutExecutableStringTable.ReadMiscStatisticNames(content.FalloutExecutablePath);
        Require(statistics.Count == 43 && FalloutMiscStatisticNames.Index(content, "Locations Discovered") == 1 &&
            FalloutMiscStatisticNames.Index(content, "Times Addicted") == 10 && statistics[5] == "Computers Hacked",
            "Owned statistic declarations differ from the tutorial's compiled operands or shared count owner.");
        var compiled = script.ReadSubrecords().Single(field => field.Signature == "SCDA").Data.Span;
        foreach (var index in new ushort[] { 1, 10 })
        {
            // Source compiled GetPCMiscStat's one enum argument. The opcode and
            // operand extent are checked independently of the name table.
            byte[] call = [0x38, 0x11, 4, 0, 1, 0, (byte)index, 0];
            Require(compiled.IndexOf(call) >= 0, "Owned tutorial has no matching compiled statistic operand.");
        }
        var emitted = new List<FalloutFormKey>();
        FalloutQuestStages? stages = null;
        var healthReads = 0;
        var inventoryQueries = new FalloutInventoryCommands(records, world, inventory, () => 1);
        var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            if (effect.Kind == FalloutReferenceEffectKind.SetStage) stages!.Enter(effect.Target!.Value, effect.Stage);
            else if (effect.Kind == FalloutReferenceEffectKind.Message)
            {
                emitted.Add(effect.Target!.Value);
                messages.ShowMessage(effect.Target.Value, script.FormKey);
            }
            else throw new NotSupportedException($"Tutorial component audit reached native effect {effect.Kind}.");
        }, Globals: globals, IsInCombat: _ => false,
            ActorValue: (_, name) => name.ToLowerInvariant() switch
            {
                "radiationrads" => vitals.State.RadiationRads,
                "perceptioncondition" => body.LimbCondition(25, vitals.State.MaximumHitPoints, vitals.State.LimbDamage),
                "endurancecondition" => body.LimbCondition(26, vitals.State.MaximumHitPoints, vitals.State.LimbDamage),
                "leftattackcondition" => body.LimbCondition(27, vitals.State.MaximumHitPoints, vitals.State.LimbDamage),
                "rightattackcondition" => body.LimbCondition(28, vitals.State.MaximumHitPoints, vitals.State.LimbDamage),
                "leftmobilitycondition" => body.LimbCondition(29, vitals.State.MaximumHitPoints, vitals.State.LimbDamage),
                "rightmobilitycondition" => body.LimbCondition(30, vitals.State.MaximumHitPoints, vitals.State.LimbDamage),
                _ => throw new NotSupportedException("Unbound tutorial fixture actor value " + name)
            },
            Inventory: inventoryQueries, IsDead: _ => vitals.State.HitPoints == 0,
            HealthPercentage: _ => { healthReads++; return vitals.State.ExactHitPoints / vitals.State.MaximumHitPoints; },
            MiscStatistic: name => messages.MiscStatistics.Read(FalloutMiscStatisticNames.Index(content, name)),
            IsSneaking: _ => false));
        stages = new(records, quests, executor.StageSteps, condition =>
            FalloutPlatformConditions.Evaluate(condition) ?? quests.Evaluate(condition));
        executor.ExecuteProgram(owner, script, program, 1);
        Require(healthReads == 1 && emitted.Count == 0, "Healthy source tutorial did not finish its actual GameMode body.");
        var menuProgram = FalloutGameModeProgram.Read(source.Span, "MenuMode");
        var menuBefore = JsonSerializer.Serialize(quests.Capture());
        foreach (var menu in new uint[] { 1007, 1013 })
        {
            using var menuScope = world.Menus.Enter(menu);
            executor.ExecuteProgram(owner, script, menuProgram, 1);
        }
        Require(JsonSerializer.Serialize(quests.Capture()) == menuBefore && emitted.Count == 0,
            "Loading or session menu selected an unrelated source Pip-Boy/VATS tutorial event.");
        foreach (var stage in new short[] { 2, 20, 22 }) stages.Enter(owner.FormKey, stage);
        Require(emitted.Count >= 3 && inventory.Notifications.Capture().Pending.Count == emitted.Count &&
            emitted.All(key => !FalloutSourceMessage.Read(records.GetEffective(key)).Modal),
            "Authored movement and grab stage messages did not reach the shared HUD queue.");
        var snapshot = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!;
        var retained = new FalloutQuestState(records); retained.Restore(snapshot);
        Require(retained.StageDone(owner.FormKey, 2) && retained.StageDone(owner.FormKey, 20) && retained.StageDone(owner.FormKey, 22),
            "Tutorial source stage receipts did not survive cold state reconstruction.");
        Require(executor.CanRetryPlayerDeathRead(owner, script, program),
            "Owned source first-read guard did not admit safe legacy query recovery.");
        var weapon = FalloutDialogueTopic.Find(records, "WEAP", "WeapNVVarmintRifle").FormKey;
        inventory.Add(records, weapon, 1, 1, true, globals, new(1, Condition: .2f));
        inventory.Equip(records, weapon);
        Require(inventoryQueries.WeaponHealthPercentage(records.RuntimeFormKey(0x14)) == 20,
            "Weapon condition query did not read the same carried variant as shared weapon damage/wear.");
        var messagesBeforeRepair = emitted.Count;
        executor.ExecuteProgram(owner, script, program, 1);
        Require(quests.StageDone(owner.FormKey, 218) && emitted.Count > messagesBeforeRepair,
            "Owned low-condition weapon did not produce the source repair tutorial stage/message.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-tutorial-query-audit/v1",
            quest = owner.FormKey, script = script.FormKey,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(source.Span)),
            fullHealthyGameMode = true, statisticNames = statistics.Count, compiledStatisticIndices = new[] { 1, 10 },
            sourceStageMessages = emitted.Count, hudQueue = true, coldStages = true, initialReadRecoveryAdmitted = true,
            boundary = "source-script-and-shared-state-component;ordinary-input-tutorial-triggers-final-pixels-and-retail-parity-unverified"
        }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
