using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutNativeSavedItem(
    uint RuntimeFormId,
    string EditorId,
    string RecordType,
    int Count);

internal sealed record FalloutNativeCampaignState(
    string Schema,
    string SaveCompatibilityId,
    FalloutFormKey ActiveCell,
    string QuestEditorId,
    short Stage,
    string PlayerName,
    FalloutNativeRaceSexSelection Character,
    FalloutNativeSpecialState Special,
    IReadOnlyList<FalloutNativeSkillIdentity> TagSkills,
    IReadOnlyList<FalloutNativeTraitIdentity> Traits,
    IReadOnlyList<FalloutNativeSavedItem> Inventory,
    IReadOnlyList<uint> EquippedRuntimeFormIds,
    IReadOnlyList<bool> PlayerControls,
    IReadOnlyList<float> PlayerPosition,
    IReadOnlyList<float> PlayerRotation);

internal sealed record FalloutNativeCampaignRestore(
    FalloutNativeCampaignState State,
    FalloutCampaignInventory Inventory);

internal static class FalloutNativeCampaignSave
{
    internal const string ExpectedSchema = "opennv-native-fnv-campaign-save/v6";
    internal const string OpeningQuestEditorId = "VCG01";
    internal const short CompletedOpeningStage = 200;
    private const int PositionComponents = 3;
    private const int RotationComponents = 4;
    private const int PlayerControlCount = 7;
    private const int RolloverTextControlIndex = 5;
    private const int SneakingControlIndex = 6;
    private const float MinimumUnitQuaternionLengthSquared = 0.999f;
    private const float MaximumUnitQuaternionLengthSquared = 1.001f;

    internal static FalloutNativeCampaignState Capture(
        string saveCompatibilityId,
        FalloutFormKey activeCell,
        FalloutOpeningInventoryGrant grant,
        string playerName,
        FalloutNativeRaceSexSelection character,
        FalloutNativeVigorContract vigorContract,
        FalloutNativeSpecialState special,
        FalloutNativeTagSkillContract tagSkillContract,
        IReadOnlyList<FalloutNativeSkillIdentity> tagSkills,
        FalloutNativeTraitFarewellContract traitFarewellContract,
        IReadOnlyList<FalloutNativeTraitIdentity> traits,
        FalloutPlayerControlState playerControls,
        IReadOnlyList<float> playerPosition,
        IReadOnlyList<float> playerRotation)
    {
        ArgumentNullException.ThrowIfNull(grant);
        FalloutNativeVigorResolver.Validate(vigorContract, special);
        FalloutNativeTagSkillResolver.Validate(tagSkillContract, tagSkills);
        FalloutNativeTraitFarewellResolver.ValidateTraits(traitFarewellContract, traits);
        var state = new FalloutNativeCampaignState(
            ExpectedSchema,
            saveCompatibilityId,
            activeCell,
            OpeningQuestEditorId,
            CompletedOpeningStage,
            playerName,
            character,
            special,
            tagSkills.OrderBy(value => value.RuntimeFormId).ToArray(),
            traits.OrderBy(value => value.RuntimeFormId).ToArray(),
            grant.Inventory.Items.OrderBy(value => value.RuntimeFormId)
                .Select(value => new FalloutNativeSavedItem(
                    value.RuntimeFormId,
                    value.EditorId,
                    value.RecordType,
                    value.Count))
                .ToArray(),
            grant.EquippedRuntimeFormIds.Order().ToArray(),
            [
                playerControls.Movement,
                playerControls.PipBoy,
                playerControls.Fighting,
                playerControls.PointOfView,
                playerControls.Looking,
                playerControls.RolloverText,
                playerControls.Sneaking,
            ],
            playerPosition.ToArray(),
            playerRotation.ToArray());
        Validate(state, saveCompatibilityId);
        return state;
    }

    internal static void Write(string path, FalloutNativeCampaignState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        Validate(state, state.SaveCompatibilityId);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static FalloutNativeCampaignRestore Read(
        string path,
        string expectedSaveCompatibilityId,
        FalloutPluginStack stack,
        FalloutNativeVigorContract vigorContract,
        FalloutNativeTagSkillContract tagSkillContract,
        FalloutOpeningInventoryGrant openingGrant,
        FalloutNativeTraitFarewellContract traitFarewellContract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(stack);
        var fullPath = Path.GetFullPath(path);
        var state = JsonSerializer.Deserialize<FalloutNativeCampaignState>(
                File.ReadAllText(fullPath)) ??
            throw new InvalidDataException($"Native campaign save is empty: {fullPath}");
        Validate(state, expectedSaveCompatibilityId);
        var activeCell = stack.GetEffective(state.ActiveCell);
        if (activeCell.Signature != "CELL")
            throw new InvalidDataException(
                "Native campaign save active CELL differs from the live winning records.");
        var characterContract = FalloutNativeRaceSexResolver.Resolve(stack);
        FalloutNativeRaceSexResolver.Validate(characterContract, state.Character);
        FalloutNativeVigorResolver.Validate(vigorContract, state.Special);
        FalloutNativeTagSkillResolver.Validate(tagSkillContract, state.TagSkills);
        FalloutNativeTraitFarewellResolver.ValidateTraits(traitFarewellContract, state.Traits);
        var expectedGrant = FalloutNativeTraitFarewellResolver.ResolveGrant(
            traitFarewellContract,
            openingGrant,
            state.TagSkills);
        var inventory = FalloutCampaignInventoryResolver.Resolve(
            stack,
            state.Inventory.Select(value => new FalloutCampaignInventoryRequest(
                value.RuntimeFormId,
                value.EditorId,
                value.RecordType,
                value.Count)).ToArray(),
            null);
        if (!inventory.Items.OrderBy(value => value.RuntimeFormId)
                .Select(value => (value.RuntimeFormId, value.EditorId, value.RecordType, value.Count))
                .SequenceEqual(state.Inventory.OrderBy(value => value.RuntimeFormId)
                    .Select(value =>
                        (value.RuntimeFormId, value.EditorId, value.RecordType, value.Count))))
            throw new InvalidDataException(
                "Native campaign save inventory differs from the live winning records.");
        if (!state.Inventory.OrderBy(value => value.RuntimeFormId)
                .Select(value => (value.RuntimeFormId, value.EditorId, value.RecordType, value.Count))
                .SequenceEqual(expectedGrant.Inventory.Items.OrderBy(value => value.RuntimeFormId)
                    .Select(value =>
                        (value.RuntimeFormId, value.EditorId, value.RecordType, value.Count))) ||
            !state.EquippedRuntimeFormIds.Order()
                .SequenceEqual(expectedGrant.EquippedRuntimeFormIds.Order()))
            throw new InvalidDataException(
                "Native campaign save loadout differs from the live farewell/tag-skill contract.");
        return new FalloutNativeCampaignRestore(state, inventory);
    }

    internal static FalloutNativeCampaignState WithWorldState(
        FalloutNativeCampaignState state,
        FalloutFormKey activeCell,
        IReadOnlyList<float> playerPosition,
        IReadOnlyList<float> playerRotation)
    {
        ArgumentNullException.ThrowIfNull(state);
        var updated = state with
        {
            ActiveCell = activeCell,
            PlayerPosition = playerPosition.ToArray(),
            PlayerRotation = playerRotation.ToArray(),
        };
        Validate(updated, state.SaveCompatibilityId);
        return updated;
    }

    internal static FalloutPlayerControlState RestorePlayerControls(
        FalloutNativeCampaignState state)
    {
        Validate(state, state.SaveCompatibilityId);
        return new FalloutPlayerControlState(
            state.PlayerControls[0],
            state.PlayerControls[1],
            state.PlayerControls[2],
            state.PlayerControls[3],
            state.PlayerControls[4],
            state.PlayerControls[RolloverTextControlIndex],
            state.PlayerControls[SneakingControlIndex]);
    }

    private static void Validate(
        FalloutNativeCampaignState state,
        string expectedSaveCompatibilityId)
    {
        if (state.Schema != ExpectedSchema ||
            string.IsNullOrWhiteSpace(expectedSaveCompatibilityId) ||
            state.SaveCompatibilityId != expectedSaveCompatibilityId ||
            string.IsNullOrWhiteSpace(state.ActiveCell.OwnerPlugin) ||
            state.ActiveCell.ObjectId == 0 ||
            state.QuestEditorId != OpeningQuestEditorId ||
            state.Stage != CompletedOpeningStage ||
            string.IsNullOrWhiteSpace(state.PlayerName) ||
            state.PlayerName != state.PlayerName.Trim() ||
            state.PlayerName.Any(char.IsControl) ||
            state.Character is null ||
            state.Character.RaceRuntimeFormId == 0 ||
            state.Character.HairRuntimeFormId == 0 ||
            state.Character.EyesRuntimeFormId == 0 ||
            string.IsNullOrWhiteSpace(state.Character.RaceEditorId) ||
            string.IsNullOrWhiteSpace(state.Character.HairEditorId) ||
            string.IsNullOrWhiteSpace(state.Character.EyesEditorId) ||
            state.Special is null ||
            state.Special.Values.Count != FalloutNativeVigorResolver.AttributeNames.Count ||
            state.TagSkills is null ||
            state.TagSkills.Count == 0 ||
            state.Traits is null ||
            state.Inventory.Count == 0 ||
            state.Inventory.Any(value =>
                value.RuntimeFormId == 0 || string.IsNullOrWhiteSpace(value.EditorId) ||
                value.RecordType.Length != FalloutPlugin.SignatureSize || value.Count <= 0) ||
            state.Inventory.Select(value => value.RuntimeFormId).Distinct().Count() !=
                state.Inventory.Count ||
            state.EquippedRuntimeFormIds.Count == 0 ||
            state.EquippedRuntimeFormIds.Distinct().Count() != state.EquippedRuntimeFormIds.Count ||
            state.EquippedRuntimeFormIds.Any(value =>
                !state.Inventory.Any(item => item.RuntimeFormId == value)) ||
            state.PlayerControls.Count != PlayerControlCount ||
            state.PlayerPosition.Count != PositionComponents ||
            state.PlayerRotation.Count != RotationComponents ||
            state.PlayerPosition.Any(value => !float.IsFinite(value)) ||
            state.PlayerRotation.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Native campaign save state is invalid.");
        var rotationLengthSquared = state.PlayerRotation.Sum(value => value * value);
        if (rotationLengthSquared is < MinimumUnitQuaternionLengthSquared or
            > MaximumUnitQuaternionLengthSquared)
            throw new InvalidDataException("Native campaign save rotation is not normalized.");
    }
}
