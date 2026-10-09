using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

if (args is ["--player-ability-supplement-contracts"])
{
    PlayerAbilitySupplementContracts.Run();
    return;
}

if (args is ["--audit-owned-player-ability", var abilityData, var abilitySpell])
{
    OwnedPlayerAbilityScriptProbe.Run(abilityData, abilitySpell);
    return;
}

if (args is ["--player-ability-script-contracts"])
{
    PlayerAbilityScriptContracts.Run();
    return;
}

if (args is ["--test-rest-menu-controls"])
{
    RestMenuControlContracts.Run();
    return;
}

if (args is ["--sleep-wait-contracts"])
{
    SleepWaitContracts.Run();
    RestHostContracts.Run();
    RestWorldConsumerContracts.Run();
    return;
}

if (args is ["--rest-host-contracts"])
{
    RestHostContracts.Run();
    return;
}

if (args is ["--test-source-rest-cues"])
{
    SourceRestCueContracts.Run();
    return;
}

if (args is ["--test-rest-interface-consumers"])
{
    RestWorldConsumerContracts.Run();
    return;
}

if (args is ["--native-source-construction-contracts"])
{
    NativeSourceConstructionContracts.Run();
    return;
}

if (args is ["--native-source-file-contracts"])
{
    NativeSourceFileContracts.Run();
    return;
}

if (args is ["--native-loaded-file-contracts"])
{
    NativeLoadedFileContracts.Run();
    return;
}

if (args is ["--actor-update-cell-process-contracts"])
{
    ActorUpdateCellProcessContracts.Run();
    return;
}

if (args is ["--actor-constructor-source-contracts"])
{
    ActorConstructorSourceContracts.Run();
    return;
}

if (args is ["--actor-process-runtime-contracts"])
{
    ActorProcessRuntimeContracts.Run();
    return;
}

if (args is ["--actor-process-contracts"])
{
    ActorProcessContracts.Run();
    return;
}

if (args is ["--actor-perception-contracts"])
{
    ActorPerceptionContracts.Run();
    return;
}

if (args is ["--combat-group-contracts"])
{
    CombatGroupContracts.Run();
    return;
}

if (args is ["--player-physical-contracts"])
{
    PlayerPhysicalActivityContracts.Run();
    return;
}

if (args is ["--experience-notification-contracts"])
{
    ExperienceNotificationContracts.Run();
    return;
}

if (args is ["--unified-save-order-contracts"])
{
    CompiledScriptContracts.SaveOrder();
    return;
}

if (args is ["--actor-script-package-contracts"])
{
    ActorScriptPackageContracts.Run();
    return;
}

if (args is ["--player-progress-contracts"])
{
    PlayerAdvancementContracts.Run();
    LevelUpPerkSourceContracts.Run();
    LevelUpMenuPublicationContracts.Run();
    return;
}

if (args is ["--compiled-opening-catalog-contracts"])
{
    CompiledScriptContracts.OpeningCatalog();
    return;
}
if (args is ["--compiled-consumer-admission-contracts"])
{
    CompiledScriptContracts.ConsumerAdmission();
    return;
}
if (args is ["--compiled-quest-recurrence-contracts"])
{
    CompiledScriptContracts.QuestRecurrence();
    return;
}
if (args.Length >= 4 && args[0] == "--audit-owned-compiled-quest-recurrence")
{
    OwnedCompiledQuestRecurrenceProbe.Run(args[1], args[2], args[3], args[4..]);
    return;
}
if (args is ["--compiled-quest-authority-contracts"])
{
    CompiledScriptContracts.QuestScheduling();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-owned-compiled-quest-authority-installation")
{
    OwnedCompiledQuestSchedulingProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args is ["--compiled-result-authority-contracts"])
{
    CompiledScriptContracts.ResultAuthority();
    return;
}
if (args is ["--compiled-nested-result-contracts"])
{
    CompiledScriptContracts.NestedResults();
    return;
}
if (args is ["--compiled-script-contracts"])
{
    CompiledScriptContracts.Run();
    return;
}
if (args.Length >= 12 && args[0] == "--audit-owned-compiled-result")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    uint Form(string value) => uint.Parse(value, System.Globalization.NumberStyles.HexNumber, invariant);
    OwnedCompiledScriptProbe.Result(args[1], args[2], args[3], new(args[4], Form(args[5])), args[6],
        new(args[7], Form(args[8])), uint.Parse(args[9], invariant), double.Parse(args[10], invariant), args[11], args[12..]);
    return;
}
if (args.Length >= 3 && args[0] == "--audit-owned-compiled-framing-installation")
{
    OwnedCompiledScriptProbe.FramingInstallation(args[1], args[2], args[3..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-owned-compiled-framing")
{
    OwnedCompiledScriptProbe.Framing(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args.Length >= 12 && args[0] == "--audit-owned-compiled-refusal")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    uint Form(string value) => uint.Parse(value, System.Globalization.NumberStyles.HexNumber, invariant);
    OwnedCompiledScriptProbe.Refusal(args[1], args[2], args[3], new(args[4], Form(args[5])), args[6],
        new(args[7], Form(args[8])), uint.Parse(args[9], invariant), args[10], args[11], args[12..]);
    return;
}
if (args is ["--reference-access-contracts"])
{
    ReferenceAccessContracts.Run();
    return;
}

if (args is ["--game-time-command-contracts"])
{
    GameTimeCommandContracts.Run();
    return;
}
if (args.Length >= 7 && args[0] == "--audit-owned-game-time")
{
    OwnedGameTimeCommandProbe.Run(args[1], args[2], args[3], args[4], args[5], args[6], args[7..]);
    return;
}
if (args is ["--corpse-equipped-loot-contracts"])
{
    CorpseEquipmentContracts.Run(lootOnly: true);
    WeaponHandlingContracts.Run();
    InventoryCommandContracts.Run(projectLocal: true);
    return;
}
if (args is ["--manual-save-preparation-contracts"])
{
    StoppedPoseContracts.Run(savePreparationOnly: true);
    return;
}
if (args is ["--corpse-equipment-contracts"])
{
    CorpseEquipmentContracts.Run();
    WeaponHandlingContracts.Run();
    StoppedPoseContracts.Run(corpseRetirementOnly: true);
    return;
}
if (args is ["--finite-sound-completion-wait-contracts"])
{
    StoppedPoseContracts.Run(finiteCompletionOnly: true);
    return;
}
if (args is ["--activation-parent-contracts"])
{
    ActivationParentContracts.Run();
    return;
}
var directory = Path.Combine(Path.GetTempPath(), "opennv-reference-contract-" + Guid.NewGuid().ToString("N"));
if (args is ["--reward-xp-contracts"])
{
    RewardXpContracts.Run();
    return;
}
if (args.Length >= 6 && args[0] == "--audit-owned-reward-xp")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    OwnedRewardXpProbe.Run(args[1], args[2], new(args[3], uint.Parse(args[4],
        System.Globalization.NumberStyles.HexNumber, invariant)), short.Parse(args[5], invariant), args[6..]);
    return;
}
if (args is ["--quest-stage-persistence-contracts"])
{
    QuestStagePersistenceContracts.Run();
    return;
}
if (args.Length >= 7 && args[0] == "--audit-owned-quest-stage-persistence")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    OwnedQuestStagePersistenceProbe.Run(args[1], args[2], args[3], new(args[4],
        uint.Parse(args[5], System.Globalization.NumberStyles.HexNumber, invariant)), short.Parse(args[6], invariant), args[7..]);
    return;
}
if (args is ["--detection-speech-contracts"])
{
    SpeechCompletionContracts.Run();
    return;
}
if (args.Length >= 11 && args[0] == "--audit-owned-detection-speech")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    uint Form(string value) => uint.Parse(value, System.Globalization.NumberStyles.HexNumber, invariant);
    OwnedDetectionSpeechProbe.Run(args[1], args[2], args[3], new(args[4], Form(args[5])),
        new(args[6], Form(args[7])), new(args[8], Form(args[9])), args[10], args[11..]);
    return;
}
if (args is ["--default-activation-contracts"])
{
    DefaultActivationContracts.Run();
    return;
}
if (args is ["--script-save-contracts"])
{
    ScriptManualSaveContracts.Run();
    HardcoreQueryContracts.Run();
    ActorAlertContracts.Run();
    return;
}
if (args.Length >= 7 && args[0] == "--audit-owned-script-save-source")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    OwnedScriptSaveSourceProbe.Run(args[1], args[2], args[3],
        new(args[4], uint.Parse(args[5], System.Globalization.NumberStyles.HexNumber, invariant)),
        args[6], args[7..]);
    return;
}
if (args is ["--terminal-contracts"])
{
    TerminalContracts.Run();
    return;
}
if (args.Length >= 8 && args[0] == "--audit-owned-terminal")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    var reference = new FalloutFormKey(args[4], uint.Parse(args[5],
        System.Globalization.NumberStyles.HexNumber, invariant));
    OwnedTerminalProbe.Run(args[1], args[2], args[3], reference,
        int.Parse(args[6], invariant), args[7], args[8..]);
    return;
}
if (args is ["--attack-variant-contracts"])
{
    AnimationResourceContracts.Run(); AttackVariantContracts.Run();
    return;
}
if (args is ["--stopped-pose-contracts"])
{
    StoppedPoseContracts.Run();
    return;
}
if (args is ["--package-data-contracts"])
{
    PackageDataContracts.Run();
    EditorTravelContracts.Run();
    TravelContracts.Run();
    return;
}
if (args.Length >= 9 && args[0] == "--audit-owned-package-data")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    uint Form(string value) => uint.Parse(value, System.Globalization.NumberStyles.HexNumber, invariant);
    OwnedPackageDataProbe.Run(args[1], args[2], args[3], new(args[4], Form(args[5])),
        new(args[6], Form(args[7])), args[8], args[9..]);
    return;
}
if (args is ["--speech-completion-contracts"])
{
    SpeechCompletionContracts.Run();
    SayToContracts.Run();
    return;
}
if (args.Length >= 15 && args[0] == "--audit-owned-speech-completion")
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    uint Form(string value) => uint.Parse(value, System.Globalization.NumberStyles.HexNumber, invariant);
    OwnedSpeechCompletionProbe.Run(args[1], args[2], args[3], new(args[4], Form(args[5])), args[6],
        new(args[7], Form(args[8])), args[9], double.Parse(args[10], invariant), double.Parse(args[11], invariant),
        new(args[12], Form(args[13])), args[14], args[15..]);
    return;
}
if (args is ["--escape-runtime-contracts"])
{
    FactionRelationContracts.Run();
    TravelContracts.Run();
    RadioConversationContracts.Run();
    PlayerScriptPackageContracts.Run();
    return;
}
if (args is ["--package-topic-contracts"])
{
    PackageEventContracts.Run();
    return;
}
if (args is ["--death-count-contracts"])
{
    ScriptDeathContracts.Run();
    StageConditionScopeContracts.Run();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-escape-stage")
{
    OwnedEscapeStageProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args is ["--reference-package-event-contracts"])
{
    ReferencePackageEventContracts.Run();
    return;
}
if (args.Length >= 6 && args[0] == "--inspect-owned-stage")
{
    var setup = new FalloutModStackSelection([new(args[1], args[2], args[6..])]).Resolve(args[3]);
    RuntimeLiveContentSource.Configure(args[3], RuntimeLiveContentSource.FalloutNewVegasGame,
        setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
    try
    {
        using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", args[4]);
        var stage = short.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture);
        var fields = quest.ReadSubrecords().ToArray();
        var begin = Array.FindIndex(fields, field => field.Signature == "INDX" && field.Data.Length == 2 &&
            BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span) == stage);
        if (begin < 0) throw new InvalidDataException("Requested source stage is absent.");
        var end = begin + 1;
        while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) ++end;
        Console.WriteLine(JsonSerializer.Serialize(new { quest = quest.FormKey, winner = quest.Plugin.Name, stage,
            sources = fields[(begin + 1)..end].Where(field => field.Signature == "SCTX")
                .Select(field => FalloutDialogueTopic.ScriptText(field.Data.Span)).ToArray() }));
    }
    finally { RuntimeLiveContentSource.Clear(); }
    return;
}
if (args is ["--script-sound-contracts"])
{
    ScriptSoundContracts.Run();
    ScriptContinuationContracts.Run();
    return;
}
if (args is ["--heading-query-contracts"])
{
    HeadingQueryContracts.Run();
    return;
}
if (args is ["--cell-query-contracts"])
{
    CellQueryContracts.Run();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-cell-query")
{
    OwnedCellQueryProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args is ["--encounter-zone-contracts"])
{
    EncounterZoneContracts.Run();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-photo-heading")
{
    OwnedHeadingProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args is ["--weapon-sight-contracts"])
{
    WeaponFiringContracts.Run();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-weapon-sight")
{
    OwnedWeaponSightProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-weapon-hit")
{
    OwnedWeaponHitProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args is ["--reference-hit-contracts"])
{
    ReferenceHitContracts.Run();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-dialogue-distance")
{
    OwnedDialogueDistanceProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-talked-to-player")
{
    OwnedTalkedToPlayerProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args is ["--dialogue-spatial-contracts"])
{
    DialogueSpatialContracts.Run();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-dialogue-form-list")
{
    OwnedDialogueFormListProbe.Run(args[1], args[2], args[3..]);
    return;
}
if (args.Length >= 8 && args[0] == "--audit-reference-door")
{
    OwnedReferenceDoorProbe.Run(args[1], args[2], args[3], args[4], args[5],
        short.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture),
        short.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture), args[8..]);
    return;
}
if (args is ["--travel-contracts"])
{
    TravelContracts.Run();
    return;
}
if (args is ["--furniture-contracts"])
{
    FurnitureContracts.Run();
    return;
}
if (args is ["--player-tag-skill-contracts"])
{
    PlayerTagSkillContracts.Run();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-player-tag-skills")
{
    OwnedPlayerTagSkillsProbe.Ttw(args[1], args[2], args[3..]);
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-furniture")
{
    OwnedFurnitureProbe.Ttw(args[1], args[2], args[3..]);
    return;
}
if (args is ["--inventory-query-contracts"])
{
    InventoryQueryContracts.Run();
    return;
}
if (args.Length >= 6 && args[0] == "--audit-dialogue-inventory")
{
    OwnedDialogueInventoryProbe.Run(args[1], args[2], args[3], args[4], args[5], args[6..]);
    return;
}
if (args is ["--vampire-contracts"])
{
    VampireQueryContracts.Run();
    return;
}
if (args.Length >= 6 && args[0] == "--audit-vampire-query")
{
    OwnedVampireQueryProbe.Run(args[1], args[2], args[3], args[4], args[5], args[6..]);
    return;
}
if (args is ["--conversation-contracts"])
{
    ConversationContracts.Run();
    ConversationSpeakerAdmissionContracts.Run();
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-immediate-dialogue")
{
    OwnedScriptedTopicSelectionProbe.ImmediateTtw(args[1], args[2], args[3..]);
    return;
}
if (args.Length >= 10 && args[0] == "--audit-scripted-topic")
{
    OwnedScriptedTopicSelectionProbe.Run(args[1], args[2], args[3], args[4], args[5], args[6],
        short.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture), args[8], args[9], args[10..]);
    return;
}
if (args.Length >= 8 && args[0] == "--audit-talking-activator")
{
    OwnedTalkingActivatorProbe.Run(args[1], args[2], args[3], args[4],
        short.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[6], args[7], args[8..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-dialogue-response-layout")
{
    OwnedDialogueResponseLayoutProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args is ["--radio-contracts"])
{
    RadioContracts.Run();
    return;
}
if (args.Length >= 2 && args[0] == "--audit-radio-hud")
{
    RadioHudDeclarationContracts.Run();
    OwnedRadioHudProbe.Run(args[1], args[2..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-radio-refresh")
{
    OwnedRadioProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args.Length >= 6 && args[0] == "--audit-radio-off")
{
    OwnedRadioOffProbe.Run(args[1], args[2], args[3], args[4], short.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[6..]);
    return;
}
if (args.Length >= 6 && args[0] == "--audit-radio-broadcast")
{
    OwnedRadioBroadcastProbe.Run(args[1], args[2], args[3], args[4], short.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[6..]);
    return;
}
if (args.Length >= 6 && args[0] == "--audit-player-reset")
{
    OwnedPlayerResetProbe.Run(args[1], args[2], args[3], args[4], short.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[6..]);
    return;
}
if (args is ["--reference-identity-contracts"])
{
    ActorSourceContracts.Run();
    return;
}
if (args.Length >= 6 && args[0] == "--audit-dialogue-identity")
{
    OwnedDialogueIdentityProbe.Run(args[1], args[2], args[3], args[4], args[5], args[6..]);
    return;
}
if (args.Length >= 6 && args[0] == "--audit-dialogue-packages")
{
    OwnedDialoguePackageQueryProbe.Run(args[1], args[2], args[3], args[4], args[5], args[6..]);
    return;
}
if (args.Length >= 3 && args[0] == "--audit-ttw-package-activation")
{
    OwnedDialoguePackageQueryProbe.TtwActivation(args[1], args[2], args[3..]);
    return;
}
if (args is ["--challenge-contracts"])
{
    ChallengeContracts.Run();
    return;
}
if (args.Length >= 5 && args[0] == "--audit-challenges")
{
    OwnedChallengeProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args is ["--age-race-contracts"])
{
    AgeRaceContracts.Run();
    ActorAppearanceContracts.Run();
    return;
}
if (args.Length >= 5 && args[0] == "--audit-age-race")
{
    OwnedAgeRaceProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args is ["--escort-contracts"])
{
    EscortContracts.Run();
    return;
}
if (args is ["--editor-travel-contracts"])
{
    EditorTravelContracts.Run();
    return;
}
if (args.Length == 1 && args[0] == "--quest-update-contracts")
{
    QuestUpdateContracts.Run();
    return;
}
if (args is ["--quest-object-contracts"])
{
    QuestObjectContracts.Run();
    return;
}
if (args.Length >= 7 && args[0] == "--audit-quest-object")
{
    OwnedQuestObjectProbe.Run(args[1], args[2], args[3], args[4], short.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[6..]);
    return;
}
if (args is ["--inventory-command-contracts"])
{
    InventoryCommandContracts.Run();
    return;
}
if (args is ["--animation-resource-contracts"])
{
    AnimationResourceContracts.Run();
    AttackVariantContracts.Run();
    return;
}
if (args is ["--audit-player-start-inventory", var inventoryRoot])
{
    OwnedInventoryProbe.Run(inventoryRoot);
    return;
}
if (args.Length >= 6 && args[0] == "--audit-inventory-commands")
{
    OwnedInventoryCommandProbe.Run(args[1], args[2], args[3], args[4], short.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[6..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-quest-updates")
{
    OwnedQuestUpdateProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-reference-access")
{
    OwnedReferenceAccessProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args.Length >= 4 && args[0] == "--audit-terminal-access")
{
    OwnedTerminalAccessProbe.Run(args[1], args[2], args[3], args[4..]);
    return;
}
if (args.Length >= 8 && args[0] == "--audit-linked-door-access")
{
    OwnedLinkedDoorAccessProbe.Run(args[1], args[2], args[3], args[4],
        uint.Parse(args[5], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture),
        args[6], uint.Parse(args[7], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture), args[8..]);
    return;
}
if (args is ["--reference-access-contracts"])
{
    ReferenceAccessContracts.Run();
    return;
}
if (args.Length == 1 && args[0] == "--player-actor-value-contracts")
{
    PlayerActorValueContracts.Run();
    return;
}
if (args.Length >= 5 && args[0] == "--audit-player-actor-values")
{
    OwnedPlayerActorValueProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-player-moves")
{
    OwnedPlayerMoveProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-loading-screens")
{
    OwnedLoadingScreenProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-character-generation")
{
    OwnedCharacterGenerationProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
if (args.Length >= 5 && args[0] == "--audit-numeric-settings")
{
    OwnedNumericGameSettingProbe.Run(args[1], args[2], args[3], args[4], args[5..]);
    return;
}
Directory.CreateDirectory(directory);
try
{
    File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Header()
        .Concat(Script(1)).Concat(Record("SCPT", 0x501, Local(1, "shared")))
        .Concat(Record("SCPT", 0x502, Local(1, "count"), Local(1, "conflictingName")))
        .Concat(Record("SCPT", 0x504, Local(7, "counter", 1, 0xA5), Local(7, "counter", 1, 0x5A)))
        .Concat(Record("SCPT", 0x505, Local(7, "counter", 1), Local(7, "counter", 0)))
        .Concat(Record("SCPT", 0x506, Local(7, "counter"), Local(8, "counter")))
        .Concat(Record("SCPT", 0x507, Local(1, "owner"), Field("SCTX", Text(
            "ref owner\nreference Owner\nbegin GameMode\nset owner to unsupported[index]\nend"))))
        .Concat(Record("SCPT", 0x508, Local(1, "owner"), Field("SCTX", Text("ref owner\nshort owner"))))
        .Concat(Record("SCPT", 0x509, Local(1, "1stFloor"), Local(2, "enabled"), Field("SCTX", Text(
            "int 1stFloor\nshort enabled // trailing author note [unparsed]"))))
        .Concat(Record("SCPT", 0x503, Field("SCTX", Text("begin OnActivate\nend"))))
        .Concat(Record("SCPT", 0x520, Local(1, "count"), Local(2, "timer"), Field("SCTX", Text(
            "begin GameMode\nif GetDisabled == 0\nset count to GetCurrentTime\nset timer to timer + 1\nendif\nend"))))
        .Concat(Record("SCPT", 0x521, Local(1, "count"), Local(2, "timer"), Field("SCTX", Text(
            "begin GameMode\nset timer to timer + 1\nset count to GetCurrentTime\nend"))))
        .Concat(Record("SCPT", 0x522, Local(1, "count"), Local(2, "timer"), Field("SCTX", Text(
            "begin OnLoad\nset count to GetRandomPercent\nset timer to timer + 1\nend"))))
        .Concat(Record("SCPT", 0x523, Local(1, "count"), Local(2, "timer"), Local(3, "failure"), Field("SCTX", Text(
            "begin OnActivate\nset count to count + 1\nend\nbegin GameMode\nset timer to 10\nend\n" +
            "begin OnActivate\nif failure == 1\nMissingOperation\nendif\nset count to count + 1\nend\n" +
            "begin OnLoad\nset timer to 20\nend"))))
        .Concat(Explosion(0xA00, flags: 0x12, damage: 28, radius: 256))
        .Concat(Explosion(0xA01, flags: 0x80, damage: 4, radius: 32))
        .Concat(Explosion(0xA02, flags: 0, damage: 4, radius: 32, force: 5))
        .Concat(Record("EXPL", 0xA03, Field("DATA", new byte[48])))
        .Concat(Explosion(0xA04, flags: 0x04, damage: 4, radius: 32))
        .Concat(Explosion(0xA05, flags: 0x08, damage: 4, radius: 32))
        .Concat(Explosion(0xA06, flags: 0x20, damage: 4, radius: 32))
        .Concat(Explosion(0xA07, flags: 0x40, damage: 4, radius: 32))
        .Concat(Explosion(0xA08, flags: 0x02, damage: 4, radius: 32))
        .Concat(Record("QUST", 0x600, Field("EDID", Text("TestQuest")), Field("SCRI", BitConverter.GetBytes(0x501u))))
        .Concat(Record("ACTI", 0x700, Field("EDID", Text("ModelLessActivator")), Field("SCRI", BitConverter.GetBytes(0x500u))))
        .Concat(Record("ACTI", 0x701, Field("SCRI", BitConverter.GetBytes(0x503u))))
        .Concat(Record("DOOR", 0x702))
        .Concat(Record("ACTI", 0x720, Field("SCRI", BitConverter.GetBytes(0x520u))))
        .Concat(Record("ACTI", 0x721, Field("SCRI", BitConverter.GetBytes(0x521u))))
        .Concat(Record("ACTI", 0x722, Field("SCRI", BitConverter.GetBytes(0x522u))))
        .Concat(Record("ACTI", 0x723, Field("SCRI", BitConverter.GetBytes(0x523u))))
        .Concat(Cell(0x803, Reference(0x920, "ReadBeforeMutation", 0x720), Reference(0x921, "EffectBeforeRead", 0x721),
            Reference(0x922, "RandomRead", 0x722)))
        .Concat(Cell(0x805, Reference(0x923, "ActivationObservation", 0x723)))
        .Concat(Cell(0x800, Reference(0x900, "FirstREF"), Reference(0x901, "SecondREF")))
        .Concat(Cell(0x801, Reference(0x902, "PeerREF")))
        .Concat(Record("CELL", 0x804, Field("DATA", [0]), Field("XCLC", new byte[8])))
        .Concat(Cell(0x802, Reference(0x903, "EmptyActivationREF", 0x701), Reference(0x904, "PlainDoorREF", 0x702),
            EnableChild(0x905, 0x904, 1), EnableChild(0x906, 0x905, 2), EnableChild(0x907, 0x908, 0), EnableChild(0x908, 0x907, 0))).ToArray());
    File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Header("Base.esm").Concat(Script(2)).ToArray());
    using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
    using (var activationWorld = new FalloutReferenceWorld(records))
    {
        activationWorld.LoadCell(FalloutCellSceneReader.Read(records, Key(0x805)));
        var executor = new FalloutReferenceScripts(records, activationWorld, new(records),
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected observation fixture effect.")));
        var instance = activationWorld.Get(Key(0x923));
        var observations = new List<string>();
        IReadOnlyList<FalloutReferenceScriptEventResult> ObserveActivation() => executor.DispatchFrame(Key(0x923),
            [new("OnLoad"), new("GameMode"), new("OnActivate", Key(0x14))], 0,
            () => observations.Add($"begin:{instance.Read(1)}:{instance.Read(2)}"),
            () => observations.Add($"end:{instance.Read(1)}:{instance.Read(2)}"));
        var result = ObserveActivation();
        Require(result.All(value => value.Error is null) && observations.SequenceEqual(new[] { "begin:0:0", "end:1:0", "begin:1:10", "end:2:10" }) &&
            instance.Read(2) == 20, "Activation observation included another source event or changed source block order.");
        observations.Clear(); instance.Write(3, 1);
        result = ObserveActivation();
        Require(result.Any(value => value.Error is not null) && observations.SequenceEqual(new[] { "begin:2:20", "end:3:20", "begin:3:10" }),
            "Failed activation block produced a successful end observation or lost its retained prefix.");
    }
    CellReviewContracts.Run(records);
    ExplosionContracts.Run(records);
    var aliasedLocals = FalloutScriptLocals.Read(records.GetEffective(Key(0x502)));
    Require(aliasedLocals.Count == 2 && aliasedLocals["count"] == 1 && aliasedLocals["conflictingName"] == 1,
        "Original local index aliases lost their independent ordered declarations.");
    var paddedLocals = FalloutScriptLocals.Read(records.GetEffective(Key(0x504)));
    Require(paddedLocals.Count == 1 && paddedLocals["COUNTER"] == 7, "Non-identity local fields changed first-match lookup.");
    var paddedScript = records.GetEffective(Key(0x504));
    var paddedStorage = new FalloutScriptLocalStorage(paddedScript, FalloutScriptLocalStorage.ReadInitialPayloads(paddedScript));
    var originalPayloads = paddedStorage.Capture().Entries.Select(entry => entry.Bits).ToArray();
    Require(originalPayloads.Length == 2 && originalPayloads[0] != originalPayloads[1],
        "The copied source initializer payloads were discarded or aliases were flattened.");
    paddedStorage.Write(7, 19);
    Require(paddedStorage.Read(7) == 19 && paddedStorage.ReadEntry(1) == originalPayloads[1],
        "A scalar assignment changed another original event-list entry with the same ID.");
    var mixedLocals = FalloutScriptLocals.ReadMetadata(records.GetEffective(Key(0x505)));
    Require(mixedLocals.Count == 2 && mixedLocals[0].StorageFlags == 1 && mixedLocals[1].StorageFlags == 0,
        "Mixed original local flags were rejected or reordered.");
    var repeatedNameScript = records.GetEffective(Key(0x506));
    var repeatedNames = FalloutScriptLocals.Read(repeatedNameScript);
    Require(repeatedNames.Count == 1 && repeatedNames["counter"] == 7 &&
        FalloutScriptLocals.ReadStorageKinds(repeatedNameScript).Keys.Order().SequenceEqual(new uint[] { 7, 8 }),
        "First-name lookup erased another source local cell.");
    Reject(() => FalloutScriptLocals.Read(records.GetEffective(Key(0x508))));
    Require(FalloutScriptLocals.ReadDeclarations(records.GetEffective(Key(0x509))).Count == 2,
        "Compiled numeric local admission rejected source name spelling or trailing author notes.");
    Require(FalloutScriptLocals.ReadDeclarations(records.GetEffective(Key(0x507)))["owner"].Kind == FalloutScriptLocalKind.Form,
        "Unsupported executable syntax prevented compiled reference-local admission.");
    var unownedArray = FalloutGameModeProgram.Read("ref owner\nbegin GameMode\nset owner to unsupported[index]\nend");
    Reject(() => unownedArray.Execute(_ => 0, (_, _) => throw new InvalidOperationException("Unowned array wrote state."),
        (_, _) => throw new InvalidOperationException("Unexpected array command."), values: new(_ => 0, (_, _) => { })));
    var firstCell = FalloutCellSceneReader.Read(records, Key(0x800));
    var secondCell = FalloutCellSceneReader.Read(records, Key(0x801));
    using (var queryWorld = new FalloutReferenceWorld(records))
    {
        queryWorld.LoadCell(firstCell); queryWorld.LoadCell(secondCell);
        Require(queryWorld.InSameCell(Key(0x900), Key(0x901), null, .5f) &&
            !queryWorld.InSameCell(Key(0x900), Key(0x902), null, .5f), "Actor cell queries incorrectly required a player placement.");
        var clockGlobals = new OpenNV.Runtime.Gameplay.State.FalloutGlobalState(
            [new(Key(0x38), "GameHour", (byte)'s', 12.5f, "synthetic")]);
        var playerPlacement = new FalloutReferencePlacement(Key(0x800), [3, 4, 12], [0, 0, 0]);
        var queryScripts = new FalloutReferenceScripts(records, queryWorld, new(records),
            new((_, _) => false, _ => { }, Globals: clockGlobals,
                Distance: (a, b) => queryWorld.Distance(a, b, playerPlacement, .5f)));
        var queries = FalloutGameModeProgram.Read("begin GameMode\nset count to GetDistance player\nset timer to GetCurrentTime\nend");
        void Query() => queryScripts.ExecuteProgram(records.GetEffective(Key(0x900)), records.GetEffective(Key(0x500)), queries, 0);
        Query();
        Require(queryWorld.Get(Key(0x900)).Read(1) == 13 && queryWorld.Get(Key(0x900)).Read(2) == 12.5,
            "Source distance or time query ignored three-dimensional game units or fractional clock time.");
        queryWorld.SetPlacement(Key(0x900), new(Key(0x800), [3, 4, 0], [0, 0, 0]));
        clockGlobals.Set(Key(0x38), 6.75f); Query();
        Require(queryWorld.Get(Key(0x900)).Read(1) == 12 && queryWorld.Get(Key(0x900)).Read(2) == 6.75,
            "Script spatial/clock query retained stale state.");
        queryWorld.Get(Key(0x900)).CaptureEngagement = () => new(Key(0x14), Position: [1.5f, 6, -2]);
        Query();
        Require(queryWorld.Get(Key(0x900)).Read(1) == 0, "Distance ignored live actor motion or converted the source axes twice.");
        Require(queryWorld.Distance(Key(0x900), Key(0x902), null, .5f) == float.MaxValue,
            "Unrelated interior distance lost its finite no-distance result.");
        InteriorQueryContracts.Verify(records, queryWorld);
    }
    using var world = new FalloutReferenceWorld(records);
    ScriptRecoveryContracts.Verify(records);
    var first = world.LoadCell(firstCell);
    var peer = world.LoadCell(secondCell).Single();
    Require(world.InstanceCount == 3 && firstCell.BaseObjects.Values.All(value => value.ModelPath is null), "Model-less reference lifetime failed.");
    Require(ReferenceEquals(first[0].Script, first[1].Script) && ReferenceEquals(first[0].Script, peer.Script), "Script definitions are not reused.");
    var quests = new FalloutQuestState(records);
    var effects = new List<FalloutReferenceScriptEffect>();
    var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
    {
        effects.Add(effect);
        if (effect.Kind == FalloutReferenceEffectKind.SetStage) quests.EnterStage(effect.Target!.Value, effect.Stage);
    }));
    Require(scripts.Dispatch(Key(0x900), "OnTriggerEnter", Key(0x14)).Blocks == 2, "Filtered and unfiltered source event order failed.");
    Require(first[0].Read(1) == 2 && first[1].Read(1) == 0 && peer.Read(1) == 1 && quests.Variable(Key(0x600), 1) == 2,
        "Winning override, per-instance isolation, cross-cell reference write or ordered quest write failed.");
    Require(scripts.Dispatch(Key(0x901), "OnTriggerEnter", Key(0x902)).Blocks == 2 && first[1].Read(1) == 0 && first[1].Read(2) == 5,
        "Mismatched event filter ran or matching blocks lost source order.");
    first[0].Write(2, 1.0000000000000002);
    for (var iteration = 0; iteration < 30; ++iteration)
    {
        scripts.UnloadCell(Key(0x800)); world.UnloadCell(Key(0x800));
        Reject(() => scripts.Dispatch(Key(0x900), "GameMode"));
        world.LoadCell(firstCell);
    }
    Require(world.InstanceCount == 3 && world.ScriptDefinitionCount == 1 && first[0].Read(2) == 1.0000000000000002,
        "Cell teardown grew or reset mutable/reference state.");
    using (var spatial = new FalloutReferenceWorld(records))
    {
        spatial.LoadCell(firstCell);
        spatial.Get(Key(0x900)).DeletePending = true;
        // A neighboring spatial grid can overlap the same persistent source references.
        spatial.LoadCell(secondCell with { References = secondCell.References.Concat(firstCell.References).ToArray() });
        Require(spatial.Get(Key(0x900)).DeletePending && !spatial.Get(Key(0x900)).Deleted,
            "Overlapping residency committed deletion while the source reference was still loaded.");
        spatial.UnloadCell(firstCell.Cell.FormKey);
        Require(spatial.IsResident(Key(0x900)) && spatial.ResidentInstances.Count() == 3 &&
            spatial.Get(Key(0x900)).Cell == firstCell.Cell.FormKey && !spatial.Get(Key(0x900)).Deleted,
            "Spatial residency changed source ancestry, duplicated events or deleted a still-resident reference.");
        spatial.UnloadCell(secondCell.Cell.FormKey);
        Require(!spatial.IsResident(Key(0x900)) && spatial.Get(Key(0x900)).Deleted,
            "Last spatial unload did not commit the retained tombstone.");
    }
    first[0].Write(1, 6);
    var failure = scripts.Dispatch(Key(0x900), "GameMode");
    Require(failure.Error?.Contains("MissingOperation", StringComparison.Ordinal) == true && first[0].Read(1) == 7,
        "Reached unsupported command lost the executed prefix or explicit failure.");
    Require(scripts.Dispatch(Key(0x901), "GameMode").Error is null, "One instance's failure poisoned another instance of the same script.");
    var soundRandom = world.Get(Key(0x900)).SoundRandom;
    soundRandom.Restore(47);
    _ = soundRandom.NextBounded(10);
    Require(world.Get(Key(0x901)).Capture().SoundRandomState is null,
        "Using one reference's sound random state initialized another reference.");
    var saved = JsonSerializer.Serialize(world.Capture());
    var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(saved)!;
    using var restored = new FalloutReferenceWorld(records);
    restored.Restore(snapshots);
    restored.LoadCell(firstCell); restored.LoadCell(secondCell);
    Require(JsonSerializer.Serialize(restored.Capture()) == saved, "Cold state changed local Float64 bits or fault identity.");
    Require(restored.Get(Key(0x900)).SoundRandom.NextBounded(1000) == soundRandom.NextBounded(1000),
        "Cold restoration changed the next sound variation choice.");
    var coldScripts = new FalloutReferenceScripts(records, restored, quests, new((_, _) => false, _ => { }));
    Require(coldScripts.Dispatch(Key(0x900), "GameMode").Error == failure.Error && restored.Get(Key(0x900)).Read(1) == 7,
        "Cold restoration reran a failed block or discarded its error.");
    using var reparsed = new FalloutReferenceWorld(records);
    var priorParseFailure = snapshots.Single(snapshot => snapshot.Reference == Key(0x901)) with
    { ScriptError = "Parse: Script contains an unbound token." };
    reparsed.Restore([priorParseFailure]);
    reparsed.LoadCell(firstCell);
    var reparsedScripts = new FalloutReferenceScripts(records, reparsed, quests, new((_, _) => false, _ => { }));
    Require(reparsedScripts.Dispatch(Key(0x901), "GameMode").Error == priorParseFailure.ScriptError &&
        reparsed.Get(Key(0x901)).Read(2) == priorParseFailure.Variables[2],
        "Cold restoration retried a retained parse rejection or reset script locals.");
    using var rejected = new FalloutReferenceWorld(records);
    Reject(() => rejected.Restore([snapshots[0], snapshots[0]]));
    Require(rejected.InstanceCount == 0, "Failed restore partially published reference state.");
    Reject(() => rejected.Restore([snapshots[0] with { Variables = new Dictionary<uint, double> { [1] = double.NaN, [2] = 0 } }]));
    using var originalRecords = FalloutPluginStack.Load(directory, ["Base.esm"]);
    using var wrongSource = new FalloutReferenceWorld(originalRecords);
    Reject(() => wrongSource.Restore(snapshots));
    Require(wrongSource.InstanceCount == 0, "Changed winning script source was admitted during restore.");
    world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x802)));
    Require(scripts.Activate(Key(0x903), Key(0x14)) is { Blocks: 1, Error: null } && effects.Count == 0,
        "An empty authored OnActivate failed to suppress default activation.");
    Require(scripts.Activate(Key(0x904), Key(0x14)) is { Blocks: 0, Error: null } &&
        effects.Single().Kind == FalloutReferenceEffectKind.DefaultActivate, "An unscripted object lost its default action.");
    effects.Clear();
    var failingDefault = new FalloutReferenceScripts(records, world, quests,
        new((_, _) => false, _ => throw new NotSupportedException("Transient native action failure")));
    Require(failingDefault.Activate(Key(0x904), Key(0x14)).Error is not null && world.Get(Key(0x904)).ScriptError is null,
        "Failed native activation poisoned an absent source program.");
    Require(scripts.Activate(Key(0x904), Key(0x14)).Error is null, "Native activation could not be retried.");
    effects.Clear();
    var capabilityAvailable = false;
    using var faultWorld = new FalloutReferenceWorld(records);
    faultWorld.LoadCell(FalloutCellSceneReader.Read(records, Key(0x801)));
    var faultCalls = 0;
    var retrying = new FalloutReferenceScripts(records, faultWorld, new(records), new((_, _) => false, _ =>
    { ++faultCalls; if (!capabilityAvailable) throw new NotSupportedException("Missing runtime capability"); }));
    var sourceFailure = retrying.Activate(Key(0x902), Key(0x14)).Error;
    Require(sourceFailure is not null, "Unsupported scripted activation did not fail closed.");
    var consumedFaultState = JsonSerializer.Serialize(faultWorld.Get(Key(0x902)).Capture());
    capabilityAvailable = true;
    Require(retrying.Dispatch(Key(0x902), "GameMode").Error is not null, "A failed interaction retried automatically on the frame clock.");
    Require(retrying.Activate(Key(0x902), Key(0x14)).Error == sourceFailure && faultCalls == 1 &&
        JsonSerializer.Serialize(faultWorld.Get(Key(0x902)).Capture()) == consumedFaultState,
        "Fresh input acknowledged a failed source invocation or replayed its consumed prefix.");
    Require(scripts.Activate(Key(0x902), Key(0x14)) is { Blocks: 1, Error: null },
        "Activation filtered its ignored header argument or rejected instance-local writes.");
    Require(effects.Select(effect => effect.Kind).SequenceEqual(new[] { FalloutReferenceEffectKind.SetStage,
        FalloutReferenceEffectKind.SpecialMenu, FalloutReferenceEffectKind.DefaultActivate }) && effects[1].Value == 42 &&
        peer.Read(2) == 42 && quests.StageDone(Key(0x600), 19),
        "Synchronous effect/query order, source menu argument or default activation bypass failed.");
    var contacts = new FalloutTriggerContacts();
    var contactFrame = contacts.Advance([Key(0x14), Key(0x902)]);
    Require(contactFrame[0] is { Name: "OnTriggerEnter", ActionReference: { ObjectId: 0x14 } } &&
        contactFrame.Single(value => value.Name == "OnTrigger").TriggerReferences!.SetEquals([Key(0x14)]),
        "Simultaneous contacts were admitted in one frame.");
    contactFrame = contacts.Advance([Key(0x902)]);
    Require(contactFrame[0] is { Name: "OnTriggerEnter", ActionReference: { ObjectId: 0x902 } } &&
        !contactFrame.Any(value => value.Name == "OnTriggerLeave"), "Leave did not defer behind an enter.");
    contactFrame = contacts.Advance([Key(0x902)]);
    Require(contactFrame[0] is { Name: "OnTriggerLeave", ActionReference: { ObjectId: 0x14 } }, "Deferred departure was lost.");
    Require(contacts.Advance([]).Single() is { Name: "OnTriggerLeave", ActionReference: { ObjectId: 0x902 } } &&
        contacts.Advance([]).Count == 0, "Trigger exit repeated or retained a stale occupant.");
    Reject(() => contacts.Advance([Key(0x14), Key(0x14)]));
    Require(scripts.DispatchFrame(Key(0x902), [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { Key(0x14) })], 0)
        .Single() is { Blocks: 1, Error: null } && peer.Read(2) == 43, "OnTrigger lost membership or manufactured an action reference.");
    PrimitiveContracts(records);
    Require(world.IsEnabled(Key(0x904)) && !world.IsEnabled(Key(0x905)) && !world.IsEnabled(Key(0x906)),
        "Initial opposite/recursive enable state or XESP padding was decoded incorrectly.");
    Require(world.SetEnabled(Key(0x904), false) && world.IsEnabled(Key(0x904)), "Script disable applied before the world update.");
    world.AdvanceEnableChanges(0, new(1.2f, 2), _ => false);
    Require(world.IsEnabled(Key(0x905)) && world.IsEnabled(Key(0x906)),
        "Parent change did not propagate through source enable relationships.");
    Require(!world.SetEnabled(Key(0x905), false) && world.IsEnabled(Key(0x905)), "A child independently overrode its enable parent.");
    using (var fades = new FalloutReferenceWorld(records))
    {
        var reference = Key(0x904);
        var times = new FalloutReferenceFadeSettings(2, 4);
        fades.SetEnabled(reference, false);
        fades.AdvanceEnableChanges(0, times, _ => true);
        fades.SetEnabled(reference, true, true);
        Require(!fades.IsEnabled(reference), "Enable bypassed the request queue.");
        fades.AdvanceEnableChanges(.1, times, _ => true);
        Require(fades.IsEnabled(reference) && fades.Get(reference).Opacity == .05f, "Fade-in lost its source rate or initial opacity.");
        fades.AdvanceEnableChanges(20, times, _ => true);
        Require(fades.Get(reference).Opacity == .15f, "A long frame bypassed the opacity step ceiling.");
        fades.SetEnabled(reference, false, true);
        fades.AdvanceEnableChanges(.1, times, _ => true);
        Require(fades.IsEnabled(reference) && fades.Get(reference).Opacity == .125f, "Fading disable lost collision/enabled state before opacity completion.");
        using var restoredFade = new FalloutReferenceWorld(records);
        restoredFade.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(fades.Capture()))!);
        for (var i = 0; i < 8; i++)
        {
            fades.AdvanceEnableChanges(.1, times, _ => true);
            restoredFade.AdvanceEnableChanges(.1, times, _ => true);
        }
        Require(!fades.IsEnabled(reference) && JsonSerializer.Serialize(fades.Capture()) == JsonSerializer.Serialize(restoredFade.Capture()),
            "Pending fade did not complete identically after cold restoration.");
        fades.SetEnabled(reference, true, true);
        fades.AdvanceEnableChanges(.1, times, _ => true);
        fades.SetEnabled(reference, false, true);
        fades.SetEnabled(reference, true, true);
        fades.AdvanceEnableChanges(.1, times, _ => true);
        Require(fades.IsEnabled(reference) && fades.Get(reference).Opacity == .1f, "Enable did not reverse the pending fade from current opacity.");
        fades.SetEnabled(reference, false, true);
        fades.AdvanceEnableChanges(0, times, _ => false);
        Require(!fades.IsEnabled(reference), "An unloaded fade node held an enable request forever.");
        Console.WriteLine("OPENNV_REFERENCE_FADE_CONTRACT_PASS queued=true rates=true stepCeiling=true collisionLifetime=true reversal=true coldPending=true absentNode=true");
    }
    var results = new FalloutMessageResults();
    var request = results.Begin(Key(0x800), Key(0x900));
    Require(results.IsPending(request) && results.Take(Key(0x900)) == -1 && results.Select(request, 3) &&
        !results.IsPending(request) && !results.Select(request, 1) && results.Take(Key(0x902)) == -1,
        "A message result was available before input or consumed by another reference.");
    var coldResults = new FalloutMessageResults();
    coldResults.Restore(JsonSerializer.Deserialize<FalloutMessageResultsSnapshot>(JsonSerializer.Serialize(results.Capture()))!);
    Require(coldResults.Take(Key(0x900)) == 3 && coldResults.Take(Key(0x900)) == -1, "Cold message result was lost or not consumed once.");
    var replacement = results.Begin(Key(0x800), Key(0x902));
    Require(!results.IsPending(request) && results.IsPending(replacement) && !results.Select(request, 0) &&
        results.Take(Key(0x900)) == -1 && results.Select(replacement, 2) && results.Take(Key(0x902)) == 2 && !results.IsPending(replacement),
        "A replaced message callback corrupted the current result slot.");
    Console.WriteLine("OPENNV_MESSAGE_RESULT_CONTRACT_PASS callerIsolation=true consumedOnce=true replaced=true staleCallback=true coldResult=true");
    Reject(() => world.IsEnabled(Key(0x907)));
    using var enabledCold = new FalloutReferenceWorld(records);
    enabledCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
    Require(!enabledCold.IsEnabled(Key(0x904)) && enabledCold.IsEnabled(Key(0x906)), "Cold state lost parent enable changes.");
    Console.WriteLine("OPENNV_REFERENCE_SCRIPT_CONTRACT_PASS modelLess=true winningOverride=true instanceIsolation=true eventFilters=true sourceOrder=true crossCell=true teardown=true coldState=true explicitFailure=true sourceDriftRejected=true");
    Console.WriteLine("OPENNV_REFERENCE_INTERACTION_CONTRACT_PASS activationDefault=true synchronousEffects=true localWrites=true stageDone=true menuArgument=true deferredContacts=true triggerActionRef=true noInventedPrimitive=true");
    Console.WriteLine("OPENNV_REFERENCE_ENABLE_CONTRACT_PASS sourceParent=true opposite=true nested=true padding=true childNoOp=true cycleRejected=true coldState=true");
}
finally
{
    File.Delete(Path.Combine(directory, "Base.esm"));
    File.Delete(Path.Combine(directory, "Patch.esp"));
    Directory.Delete(directory);
}

CompiledScriptContracts.Run();
ActorScriptPackageContracts.Run();
NativeSourceConstructionContracts.Run();
NativeSourceFileContracts.Run();
NativeLoadedFileContracts.Run();
CombatGroupContracts.Run();
ActorPerceptionContracts.Run();
ActorProcessContracts.Run();
ActorProcessRuntimeContracts.Run();
ActorConstructorSourceContracts.Run();
ActorUpdateCellProcessContracts.Run();
SleepWaitContracts.Run();
RestHostContracts.Run();
RestMenuControlContracts.Run();
RestWorldConsumerContracts.Run();
SourceRestCueContracts.Run();
PlayerAdvancementContracts.Run();
PlayerPhysicalActivityContracts.Run();
LevelUpPerkSourceContracts.Run();
LevelUpMenuPublicationContracts.Run();
CompiledScriptContracts.NestedResults();
CompiledScriptContracts.ResultAuthority();
CompiledScriptContracts.QuestRecurrence();
CompiledScriptContracts.ConsumerAdmission();
CompiledScriptContracts.OpeningCatalog();
ConversationContracts.Run();
ConversationSpeakerAdmissionContracts.Run();
ActorSourceContracts.Run();
FactionRelationContracts.Run();
EncounterZoneContracts.Run();
FollowPackageContracts.Run();
DialoguePackageContracts.Run();
DialogueSpatialContracts.Run();
HeadingQueryContracts.Run();
CellQueryContracts.Run();
NpcDialogueLinkContracts.Run();
RadioContracts.Run();
PatrolContracts.Run();
EscortContracts.Run();
PackageDataContracts.Run();
EditorTravelContracts.Run();
TravelContracts.Run();
FurnitureContracts.Run();
AuthoredRagdollContracts.Run();
ActorDamageContracts.Run();
StoppedPoseContracts.Run();
TerminalContracts.Run();
ScriptDeathContracts.Run();
ActivationParentContracts.Run();
DefaultActivationContracts.Run();
ScriptManualSaveContracts.Run();
HardcoreQueryContracts.Run();
GameTimeCommandContracts.Run();
ActorAlertContracts.Run();
StageConditionScopeContracts.Run();
PlayerSkillContracts.Run();
PlayerTagSkillContracts.Run();
PlayerActorValueContracts.Run();
PerkParameterContracts.Run();
InputControlContracts.Run();
PlayerMoveContracts.Run();
LoadingScreenContracts.Run();
CharacterGenerationContracts.Run();
RewardXpContracts.Run();
ExperienceNotificationContracts.Run();
PlayerScriptPackageContracts.Run();
PackageEventContracts.Run();
ReferencePackageEventContracts.Run();
ReferenceHitContracts.Run();
DoorMotionContracts.Run();
ReferenceAccessContracts.Run();
NumericGameSettingContracts.Run();
ActorAppearanceContracts.Run();
ChallengeContracts.Run();
AgeRaceContracts.Run();
FaceGeometryContracts.Run();
ScriptSoundContracts.Run();
ScriptContinuationContracts.Run();
NoActivationSoundContracts.Run();
VampireQueryContracts.Run();
InventoryQueryContracts.Run();
SayToContracts.Run();
SpeechCompletionContracts.Run();
RadioConversationContracts.Run();
ScreenBloodContracts.Run();
QuestMenuContracts.Run();
QuestUpdateContracts.Run();
IngestibleContracts.Run();
if (args is [var voiceRoot, "--voices"]) OwnedDialogueVoiceProbe.Run(voiceRoot);
if (args is [var aidRoot, "--ingestibles"]) OwnedIngestibleProbe.Run(aidRoot);
else if (args is [var companionRoot, "--companion-packages"]) OwnedCompanionPackageProbe.Run(companionRoot);
else if (args is [var companionGameplayRoot, "--companion-gameplay"]) OwnedCompanionGameplayProbe.Run(companionGameplayRoot);
else if (args is [var zoneRoot, "--encounter-zones", var zoneCell, var zoneSave, var zoneOutput])
    OwnedEncounterZoneProbe.Run(zoneRoot, zoneCell, zoneSave, zoneOutput);
else if (args is [var patrolRoot, "--patrols", var patrolOutput]) OwnedPatrolProbe.Run(patrolRoot, patrolOutput);
else if (args is [var recoveryRoot, "--script-recovery", var recoverySave, var recoveryOutput])
    OwnedScriptRecoveryProbe.Run(recoveryRoot, recoverySave, recoveryOutput);
StageAndInventoryContracts.Run();
PlayerAbilityScriptContracts.Run();
PlayerAbilitySupplementContracts.Run();
QuestStagePersistenceContracts.Run();
QuestObjectContracts.Run();
InventoryCommandContracts.Run();
AnimationResourceContracts.Run();
AttackVariantContracts.Run();
WeaponHandlingContracts.Run();
WeaponFiringContracts.Run();
DestructionContracts.Run();
var disabledControls = new FalloutPlayerControlState(false, false, false, false, false, false, false);
Require(new FalloutPlayerControlCommand(true, []).Apply(disabledControls) == FalloutPlayerControlState.AllEnabled,
    "EnablePlayerControls without arguments did not enable all controls.");
Require(new FalloutPlayerControlCommand(true, [true, true, true, true, true, true]).Apply(disabledControls).Sneaking,
    "Omitted EnablePlayerControls flag lost its source default.");
Require(new FalloutPlayerControlCommand(false, []).Apply(FalloutPlayerControlState.AllEnabled) ==
    new FalloutPlayerControlState(false, false, false, false, true, true, true), "DisablePlayerControls source defaults changed.");

if (args is [var ownedRoot])
{
    OwnedOpeningProgramProbe.Run(ownedRoot);
    OwnedReferenceInteractionProbe.Run(ownedRoot);
    var questionnaireHistory = OwnedConversationProbe.Run(ownedRoot);
    OwnedInventoryProbe.Run(ownedRoot);
    OwnedFarewellProbe.Run(ownedRoot, questionnaireHistory);
}

static FalloutFormKey Key(uint id) => new("Base.esm", id);
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static void Reject(Action action)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
    throw new InvalidDataException("Invalid state/event was admitted.");
}
static byte[] Script(int increment)
{
    var source = $"scn ObjectScript\nshort count\nfloat timer\nbegin OnTriggerEnter player\n" +
        $"set count to count + {increment}\nset PeerREF.count to PeerREF.count + 1\nset TestQuest.shared to TestQuest.shared + count\nend\n" +
        "begin OnTriggerEnter\nset timer to timer + 1\nend\nbegin OnTriggerEnter PeerREF\nset timer to timer + 4\nend\n" +
        "begin GameMode\nif count >= 6\nset count to count + 1\nMissingOperation\nendif\nend\n" +
        $"begin OnActivate PeerREF\nset timer to {40 + increment}\nSetStage TestQuest 19\n" +
        "if GetStageDone TestQuest 19 == 1\nShowLoveTesterMenuParams timer\nendif\nActivate\nend\n" +
        "begin OnTrigger player\nif IsActionRef player == 0\nset timer to timer + 1\nendif\nend";
    return Record("SCPT", 0x500, Field("EDID", Text("ObjectScript")), Local(1, "count"), Local(1, "count"), Local(2, "timer"),
        Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCRO", BitConverter.GetBytes(0x902u)),
        Field("SCRO", BitConverter.GetBytes(0x600u)), Field("SCTX", Text(source)));
}
static byte[] Local(uint index, string name, byte flags = 0, byte padding = 0)
{
    var data = Enumerable.Repeat(padding, 24).ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(data, index);
    data[16] = flags;
    return Field("SLSD", data).Concat(Field("SCVR", Text(name))).ToArray();
}
static byte[] Reference(uint id, string name, uint baseId = 0x700) => Record("REFR", id, Field("EDID", Text(name)),
    Field("NAME", BitConverter.GetBytes(baseId)), Field("DATA", new byte[24]));
static byte[] EnableChild(uint id, uint parent, byte flags)
{
    var data = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(data, parent);
    data[4] = flags; data[5] = 0xdb; data[6] = 0x18; // Observed nonzero unused bytes must not become flags.
    return Record("REFR", id, Field("NAME", BitConverter.GetBytes(0x702u)), Field("DATA", new byte[24]), Field("XESP", data));
}
static void PrimitiveContracts(FalloutPluginStack records)
{
    Require(FalloutReferencePrimitive.Read(records.GetEffective(Key(0x900))) is null, "A reference without XPRM acquired a primitive.");
    // Byte layout and invalid extents are exercised by the native physics audit;
    // this scalar probe also checks that no primitive is invented for model-less refs.
}
static byte[] Cell(uint id, params byte[][] references)
{
    var body = references.SelectMany(bytes => bytes).ToArray();
    var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), id);
    BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); body.CopyTo(group, 24);
    return Record("CELL", id, Field("DATA", [1])).Concat(group).ToArray();
}
static byte[] Header(string? master = null)
{
    var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
    return master is null ? Record("TES4", 0, Field("HEDR", data)) :
        Record("TES4", 0, Field("HEDR", data), Field("MAST", Text(master)), Field("DATA", new byte[8]));
}
static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
static byte[] Field(string signature, byte[] data)
{
    var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
}
static byte[] Record(string signature, uint id, params byte[][] fields)
{
    var data = fields.SelectMany(bytes => bytes).ToArray();
    var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
}
static byte[] Explosion(uint id, uint flags, float damage, float radius, float force = 0)
{
    var data = new byte[52];
    BinaryPrimitives.WriteSingleLittleEndian(data, force);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), damage);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), radius);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), flags);
    return Record("EXPL", id, Field("EDID", Text("SyntheticExplosion")), Field("MODL", Text("effects/synthetic.nif")), Field("DATA", data));
}
