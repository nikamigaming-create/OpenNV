using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCompanionGameplayProbe
{
    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var globals = FalloutGlobalState.Read(records);
        var quests = new FalloutQuestState(records);
        var actor = records.RuntimeFormKey(0x1732d1);
        var broken = records.RuntimeFormKey(0x1572e6);
        world.MoveTo(actor, broken); world.InitializeActorTemplates(actor, 1, globals);
        world.Get(actor).Enabled = true;
        world.Get(actor).TalkedToPlayer = true;
        world.LoadCell(world.ComposeResidency(FalloutCellSceneReader.Read(records, world.Placement(actor).Cell)));
        var effects = new List<FalloutReferenceScriptEffect>();
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add, Globals: globals));
        world.Get(actor).Restrained = true;
        var load = scripts.Dispatch(actor, "OnLoad");
        var tick = scripts.Dispatch(actor, "GameMode", elapsedSeconds: 5.1);
        if (load.Error is not null || tick.Error is not null || tick.Blocks != 2 || world.Get(actor).Restrained)
            throw new InvalidDataException("Source companion activation timer failed: " + (load.Error ?? tick.Error));
        var greeting = records.GetEffective(records.RuntimeFormKey(0x1579d7));
        var greetingInfo = FalloutDialogueTopic.Decode(greeting);
        VerifySounds(greetingInfo);
        scripts.ExecuteResult(greetingInfo, actor, true);
        scripts.ExecuteResult(greetingInfo, actor, false);
        if (!effects.Any(effect => effect.Kind == FalloutReferenceEffectKind.ScriptPackage && effect.Argument is null))
            throw new InvalidDataException("Source greeting did not remove its script-package override.");
        var record = records.GetEffective(records.RuntimeFormKey(0x1579c3));
        var topic = record.Plugin.AdjustFormId(record.Groups.Single(group => group.Type == 7).LabelAsUInt32);
        var info = FalloutDialogueTopic.Read(records, topic).Infos.Single(info => info.Record.FormKey == record.FormKey);
        VerifySounds(info);
        scripts.ExecuteResult(info, actor, true);
        scripts.ExecuteResult(info, actor, false);
        var followerFaction = FalloutDialogueTopic.Find(records, "FACT", "FollowerFaction").FormKey;
        var teammateFaction = FalloutDialogueTopic.Find(records, "FACT", "TeammateFaction").FormKey;
        var suite = FalloutDialogueTopic.Find(records, "PERK", "CompanionSuite").FormKey;
        var sensor = FalloutDialogueTopic.Find(records, "PERK", "EnhancedSensors").FormKey;
        if (!world.Get(actor).PlayerTeammate || world.ActorFactions(actor)[followerFaction] != 1 || world.ActorFactions(actor)[teammateFaction] != 1 ||
            !world.AcquiredPerks(actor).Contains(suite) || !world.AcquiredPerks(records.RuntimeFormKey(0x14)).Contains(sensor) ||
            !world.IgnoresFriendlyHits(actor) || !world.IgnoresCrime(actor) || world.CombatStyle(actor)?.WeaponRestriction != 2 ||
            !effects.Any(effect => effect.Kind == FalloutReferenceEffectKind.EvaluatePackages))
            throw new InvalidDataException("Source recruitment result did not reach its authoritative owners.");
        var defense = world.Defense(actor, 1, globals);
        if (defense.Threshold != 8) throw new InvalidDataException("Companion source damage threshold differs.");
        var gun = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(0x166b94), false);
        if (gun.Model is not null || gun.AnimationGroup != "1hp") throw new InvalidDataException("Embedded creature weapon differs from source.");
        var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        var overrides = JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(world.CaptureActorOverrides()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(snapshots); cold.RestoreActorOverrides(overrides);
        if (!cold.Get(actor).PlayerTeammate || !cold.AcquiredPerks(actor).Contains(suite) || cold.ActorFactions(actor)[teammateFaction] != 1 ||
            cold.CombatStyle(actor)?.WeaponRestriction != 2 || !cold.IgnoresFriendlyHits(actor))
            throw new InvalidDataException("Cold restoration lost recruited actor state.");
        world.DamageActor(actor, records.RuntimeFormKey(0x14), 0, 20, 1, 1, globals);
        var combatEnd = scripts.Dispatch(actor, "OnCombatEnd");
        if (combatEnd.Error is not null || world.Health(actor).Current != world.Health(actor).Base || world.ActorValue(actor, "aggression") != 0)
            throw new InvalidDataException("Companion source combat-end recovery failed: " + combatEnd.Error);
        Console.WriteLine("OPENNV_OWNED_COMPANION_GAMEPLAY_PASS activationTimer=true greetingResults=true recruitmentResults=true factions=true perks=true embeddedWeapon=true defense=true coldRestore=true combatEnd=true ordinaryRecruitment=separate");

        void VerifySounds(FalloutDialogueInfo dialogue)
        {
            foreach (var response in dialogue.Responses.Where(response => response.Sound is not null))
            {
                var sound = FalloutSoundRecordReader.Read(records, response.Sound!.Value);
                var variants = FalloutAnimationSound.Variants(sound, content.ResourcePathsUnder(sound.LogicalPath));
                var selected = FalloutAnimationSound.Select(sound, variants, world.Get(actor).SoundRandom, stereoOutput: true);
                if (!selected.Play || !content.TryRead(selected.Path!, null, out var bytes, out _) || bytes.Length == 0)
                    throw new InvalidDataException("Companion response sound did not select an owned audible resource.");
            }
        }
    }
}
