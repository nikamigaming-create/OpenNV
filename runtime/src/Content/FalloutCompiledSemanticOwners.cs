namespace OpenNV.Runtime.Content;

// These names identify implemented public API contracts, not opcode positions
// or game selections. The selected image owns each compiled declaration; an
// admitted name still needs the actual caller, parameters and gameplay owner.
internal static class FalloutCompiledSemanticOwners
{
    private static readonly HashSet<string> Queries = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetDistance", "GetLocked", "GetPos", "GetAngle", "GetSecondsPassed", "GetActorValue",
        "GetCurrentTime", "GetButtonPressed", "GetInSameCell", "GetDisabled", "MenuMode", "GetVampire",
        "GetDead", "GetItemCount", "GetTalkedToPC", "GetQuestRunning", "GetStage", "GetStageDone",
        "GetLockLevel", "GetInCell", "GetIsSex", "GetInFaction", "GetIsID", "GetFactionRank",
        "GetRandomPercent", "GetLevel", "GetDeadCount", "GetHeadingAngle", "IsActionRef", "GetIsReference",
        "IsTalking", "GetOpenState", "GetSitting", "GetSleeping", "GetKnockedState", "IsPCSleeping", "GetIsCurrentPackage", "GetIgnoreCrime", "GetActionRef",
        "GetSelf", "GetUnconscious", "GetBaseActorValue", "IsInCombat", "IsAnimPlaying", "IsInInterior",
        "IsXBox", "GetIgnoreFriendlyHits", "GetLinkedRef", "IsInList", "GetObjectiveCompleted",
        "GetObjectiveDisplayed", "HasPerk", "GetPlayerTeammate", "GetBroadcastState", "GetMapMarkerVisible",
        "GetPermanentActorValue", "IsPS3", "IsWin32", "GetQuestCompleted", "GetInCharGen",
        "IsHardcore", "GetLocationSpecificLoadScreensOnly", "IsActorsAIOff", "GetPCSleepHours",
    };
    private static readonly HashSet<string> Effects = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddItem", "SetActorValue", "ModActorValue", "PlayGroup", "Enable", "Disable", "Say", "SayTo",
        "StartQuest", "StopQuest", "SetStage", "RemoveItem", "ShowMessage", "SetAlert", "Look", "StopLook",
        "EvaluatePackage", "EnablePlayerControls", "DisablePlayerControls", "Lock", "UnLock", "SetEnemy",
        "SetAlly", "SetFactionRank", "KillActor", "AddScriptPackage", "RemoveScriptPackage", "MoveTo",
        "RemoveAllItems", "SetCombatStyle", "SetDestroyed", "ShowRaceMenu", "SetOpenState", "SetInChargen",
        "EquipItem", "UnequipItem", "SetUnconscious", "SetRestrained", "SCAOnActor", "ForceActorValue",
        "PlayBink", "SetOwnership", "ForceWeather", "SetScale", "ResetHealth", "ReleaseWeatherOverride",
        "Autosave", "AddPerk", "RewardXP", "AddNote", "AddToFaction", "RemoveFromFaction",
        "ApplyImageSpaceModifier", "RemoveImageSpaceModifier", "SetObjectiveCompleted", "SetObjectiveDisplayed",
        "RemovePerk", "CompleteAllObjectives", "MarkForDelete", "AddItemHealthPercent", "SetPlayerTeammate",
        "SetBroadcastState", "StartRadioConversation", "MatchFaceGeometry", "AgeRace", "MatchRace",
        "SetPCYoung", "SexChange", "ResetInventory", "ResetAI", "PlayMusic",
        "SetLocationSpecificLoadScreensOnly", "ResetPipboyManager", "SetPCToddler", "ForceSave",
        "PipBoyRadioOff", "ClearScreenSplatter", "Activate", "PlaySound", "CreateDetectionEvent",
        "GetPlayerName", "SetTagSkills", "TraitMenu", "PlayIdle", "ShowRecipeMenu", "ShowBarterMenu",
        "RestoreActorValue", "IgnoreCrime", "SetIgnoreFriendlyHits", "ForceActiveQuest", "SetHardcore",
        "AutoDisplayObjectives", "AddAchievement", "SetTalkingActivatorActor", "SwapTexture", "SwapTextureOnRef",
        "ShowMap", "UnlockChallenge", "IncrementScriptedChallenge", "KillQuestUpdates", "StartConversation",
        "ShowLoveTesterMenuParams", "StopCombatAlarmOnActor", "SetActorsAI", "ToggleActorsAI", "SetPCSleepHours", "ShowSleepWaitMenu",
    };

    internal static bool IsQuery(string name) => Queries.Contains(name);
    internal static bool IsEffect(string name) => Effects.Contains(name);
    internal static bool IsMessage(string name) => name.Equals("ShowMessage", StringComparison.OrdinalIgnoreCase);
}
