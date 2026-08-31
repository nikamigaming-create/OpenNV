using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal enum GameplayUiPanel
{
    Status,
    Items,
    Data,
    Map,
    Controls,
}

internal sealed record GameplayUiInventoryItem(
    string FormId,
    string EditorId,
    string RecordType,
    int Count,
    bool Equipped);

internal sealed record GameplayUiQuest(
    string FormId,
    string EditorId,
    int Stage,
    bool Running,
    bool Stopped);

internal sealed record GameplayUiObjective(
    string QuestEditorId,
    int Index,
    string State,
    bool Enabled,
    string Text);

internal sealed record GameplayUiMapMarker(
    string FormId,
    string EditorId,
    Vector3 Position);

internal sealed record GameplayUiControl(string Label, string Binding);

internal sealed record GameplayUiSnapshot(
    string CellFormId,
    string CellEditorId,
    Vector3 PlayerPosition,
    string Status,
    string Objective,
    string PlayerName,
    bool OpeningCompleted,
    int? Level,
    int? HitPoints,
    int? MaximumHitPoints,
    int? ActionPoints,
    int? MaximumActionPoints,
    int? ExperiencePoints,
    int? NextLevelExperiencePoints,
    GameplaySession.SandboxObjectiveStage ObjectiveStage,
    string? EquippedWeaponFormId,
    string EquippedWeaponLabel,
    int AmmoInMagazine,
    int WeaponClipSize,
    int ReserveAmmo,
    IReadOnlyList<GameplayUiInventoryItem> Inventory,
    IReadOnlyList<GameplayUiQuest> Quests,
    IReadOnlyList<GameplayUiObjective> Objectives,
    IReadOnlyList<GameplayUiMapMarker> MapMarkers,
    IReadOnlyList<GameplayUiControl> Controls,
    string SavePath);
