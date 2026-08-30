using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed record Fo2CharacterStartSaveState(
    string Path,
    string Sha256,
    string SourceProfileId,
    Fo2CharacterSelection Character,
    string RuntimeProfileId,
    string RuntimeProfileSha256,
    string MapSha256,
    string WalkMaskSha256,
    int MapIndex,
    int Elevation,
    int ArrivalTile,
    int CurrentTile,
    int Rotation,
    Vector3 Position,
    string MotionMode,
    string BlockedMovementMode,
    string PresentationMode,
    Fo2ArroyoExitTransition? LastTransition,
    Fo2TempleConfrontationState? TempleConfrontation,
    Fo2TempleAppliedTransition? TempleExitTransition,
    Fo2ArroyoTrialProgressState? TrialProgress)
{
    internal const string Schema = "opennv-fo2-character-arroyo-save/v12";
    internal const string RouteMode = "chosen-one-source-exit-route-v1";
    private const string PriorSchema = "opennv-fo2-character-arroyo-save/v10";
    private const string VersionNineSchema = "opennv-fo2-character-arroyo-save/v9";
    internal const string ColorAppearanceSchema = "opennv-fo2-character-arroyo-save/v8";
    private const string ProceduralAppearanceSchema = "opennv-fo2-character-arroyo-save/v7";
    private const string FaceAppearanceSchema = "opennv-fo2-character-arroyo-save/v6";
    private const string AppearanceSchema = "opennv-fo2-character-arroyo-save/v5";
    private const string ConfrontationSchema = "opennv-fo2-character-arroyo-save/v4";
    private const string RouteSchema = "opennv-fo2-character-arroyo-save/v3";
    private const string PreviousSchema = "opennv-fo2-character-arroyo-save/v2";
    private const string PreviousRouteMode = "chosen-one-taken-to-arroyo-map-3";
    private const string LegacySchema = "opennv-fo2-character-arroyo-save/v1";
    private const string LegacyRouteMode = "owned-premade-taken-to-arroyo-map-3";

    internal static string DefaultPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "OpenNV",
        "saves",
        "fallout2",
        "character-arroyo-v1.json");

    internal static bool Exists(string configuredPath) => File.Exists(ResolvePath(configuredPath));

    internal static Fo2CharacterStartSaveState Capture(
        string configuredPath,
        Fo2CharacterStartCatalog characterStart,
        Fo2ArroyoCavesPresentationCatalog arroyo,
        Fo2TemplePresentationCatalog temple,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        Fo2CharacterSelection character,
        Fo2TempleConfrontationState? templeConfrontation,
        Fo2TempleAppliedTransition? templeExitTransition,
        Fo2TempleTransitionCatalog transitions,
        Fo2ArroyoTrialProgressState? trialProgress,
        Fo2ArroyoTrialRouteContract? trialRoute)
    {
        character.Validate(characterStart);
        if (characterStart.SourceProfileId != arroyo.SourceProfileId ||
            temple.SourceProfileId != arroyo.SourceProfileId ||
            runtime.Profile.Id != "fo2-arroyo-map-3-player-runtime-v1" ||
            runtime.Player.MotionMode != CharacterBody3D.MotionModeEnum.Grounded ||
            !runtime.Player.CanOccupy(runtime.Player.CurrentTile))
            throw new InvalidOperationException(
                "Fallout 2 character/Arroyo save state is not authoritative.");
        var position = runtime.Player.Position;
        if (!Finite(position) ||
            Fo1HexMath.NearestTile(new Vector3(position.X, 0.0f, position.Z)) !=
                runtime.Player.CurrentTile)
            throw new InvalidOperationException(
                "Fallout 2 player position does not resolve to its saved source tile.");

        var player = runtime.Player;
        Fo2ArroyoExitTransition? lastTransition = player.CurrentMapIndex switch
        {
            Fo2ArroyoCavesPresentationCatalog.MapIndex => null,
            Fo2TemplePresentationCatalog.MapIndex
                when player.CurrentMapSha256 == temple.MapSha256 &&
                    player.ArrivalTile == arroyo.LiveExit.TargetTile => arroyo.LiveExit,
            4 when trialRoute is not null &&
                player.CurrentMapSha256 == trialRoute.VillageArrival.MapSha256 &&
                player.ArrivalTile == trialRoute.VillageArrival.ArrivalTile => arroyo.LiveExit,
            _ => throw new InvalidOperationException(
                "Fallout 2 active map is outside the admitted Arroyo/Temple route."),
        };
        if (player.CurrentMapIndex == Fo2ArroyoCavesPresentationCatalog.MapIndex &&
                templeConfrontation is not null ||
            player.CurrentMapIndex is Fo2TemplePresentationCatalog.MapIndex or 4 &&
                templeConfrontation is null)
            throw new InvalidOperationException(
                "Fallout 2 confrontation state does not match the active map.");
        templeConfrontation?.Validate(
            temple.Confrontation,
            Fo2TempleConfrontationRuntime.MaximumActionPoints(character));
        if ((trialProgress is null) != (trialRoute is null))
            throw new InvalidOperationException(
                "Fallout 2 trial save requires both state and its active source route.");
        trialProgress?.Validate(trialRoute!);
        ValidateTempleExitTransition(
            templeExitTransition,
            transitions,
            player.CurrentMapIndex,
            player.CurrentTile,
            trialProgress,
            trialRoute);
        return new Fo2CharacterStartSaveState(
            ResolvePath(configuredPath),
            "",
            characterStart.SourceProfileId,
            character,
            runtime.Profile.Id,
            runtime.Profile.Sha256,
            player.CurrentMapSha256,
            player.CurrentWalkMaskSha256,
            player.CurrentMapIndex,
            player.CurrentElevation,
            player.ArrivalTile,
            player.CurrentTile,
            player.Presentation.Direction,
            position,
            runtime.Player.MotionMode.ToString(),
            runtime.Profile.BlockedMovementMode,
            runtime.Profile.PlayerPresentationMode,
            lastTransition,
            templeConfrontation,
            templeExitTransition,
            trialProgress);
    }

    internal Fo2CharacterStartSaveState Write()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        try
        {
            var state = new
            {
                schema = Schema,
                campaign = "Fallout2",
                routeMode = RouteMode,
                sourceProfileId = SourceProfileId,
                character = new
                {
                    Character.Mode,
                    Character.Id,
                    Character.Role,
                    SourceId = Character.Source.Id,
                    SourceRole = Character.Source.Role,
                    Character.GcdSha256,
                    Character.BioSha256,
                    Character.Profile.Name,
                    Character.Profile.Age,
                    Character.Profile.Sex,
                    special = Character.Profile.Special,
                    taggedSkills = Character.Profile.TaggedSkills,
                    traits = Character.Profile.Traits,
                },
                appearance = new
                {
                    Character.Appearance.Schema,
                    Character.Appearance.BasisPremadeId,
                    Character.Appearance.SourcePanelLogicalPath,
                    Character.Appearance.SourcePanelSha256,
                    Character.Appearance.LocalPanelPngSha256,
                    Character.Appearance.PreviewMode,
                    Character.Appearance.PortraitState,
                    Character.Appearance.CustomFaceEdited,
                    Character.Appearance.CustomPortraitGenerated,
                    Character.Appearance.FaceShapeId,
                    Character.Appearance.HairStyleId,
                    Character.Appearance.SkinToneId,
                    Character.Appearance.HairColorId,
                    Character.Appearance.EyeColorId,
                    Character.Appearance.BrowStyleId,
                    Character.Appearance.NoseStyleId,
                    Character.Appearance.MouthStyleId,
                    Character.Appearance.PortraitGeneratorId,
                    Character.Appearance.AppearanceRecipeId,
                    Character.Appearance.AppearanceRecipeSha256,
                    Character.Appearance.GeneratedPortraitPath,
                    Character.Appearance.GeneratedPortraitSha256,
                    Character.Appearance.GeneratedPortraitWidth,
                    Character.Appearance.GeneratedPortraitHeight,
                },
                world = new
                {
                    mapIndex = MapIndex,
                    elevation = Elevation,
                    arrivalTile = ArrivalTile,
                    currentTile = CurrentTile,
                    rotation = Rotation,
                    position = new[] { Position.X, Position.Y, Position.Z },
                    mapSha256 = MapSha256,
                    walkMaskSha256 = WalkMaskSha256,
                },
                runtime = new
                {
                    profileId = RuntimeProfileId,
                    profileSha256 = RuntimeProfileSha256,
                    motionMode = MotionMode,
                    blockedMovementMode = BlockedMovementMode,
                    presentationMode = PresentationMode,
                },
                lastTransition = LastTransition is null ? null : new
                {
                    sourceMapIndex = LastTransition.SourceMapIndex,
                    sourceMapSha256 = LastTransition.SourceMapSha256,
                    sourceTile = LastTransition.SourceTile,
                    sourceElevation = LastTransition.SourceElevation,
                    exitSerial = LastTransition.ExitSerial,
                    exitFid = LastTransition.ExitFid,
                    exitPid = LastTransition.ExitPid,
                    sourcePathSha256 = LastTransition.SourcePathSha256,
                    targetMapIndex = LastTransition.TargetMapIndex,
                    targetLogicalPath = LastTransition.TargetLogicalPath,
                    targetMapSha256 = LastTransition.TargetMapSha256,
                    targetTile = LastTransition.TargetTile,
                    targetElevation = LastTransition.TargetElevation,
                    targetRotation = LastTransition.TargetRotation,
                },
                templeConfrontation = TempleConfrontation is null ? null : new
                {
                    targetHitPoints = TempleConfrontation.TargetHitPoints,
                    playerActionPoints = TempleConfrontation.PlayerActionPoints,
                    combatActive = TempleConfrontation.CombatActive,
                    spearLooted = TempleConfrontation.SpearLooted,
                    spearEquipped = TempleConfrontation.SpearEquipped,
                },
                templeExitTransition = TempleExitTransition is null ? null : new
                {
                    exitSerial = TempleExitTransition.ExitSerial,
                    sourceMapIndex = TempleExitTransition.SourceMapIndex,
                    sourceMapSha256 = TempleExitTransition.SourceMapSha256,
                    sourceTile = TempleExitTransition.SourceTile,
                    targetMapIndex = TempleExitTransition.TargetMapIndex,
                    targetMapSha256 = TempleExitTransition.TargetMapSha256,
                    targetMapName = TempleExitTransition.TargetMapName,
                    targetTile = TempleExitTransition.TargetTile,
                    targetElevation = TempleExitTransition.TargetElevation,
                    targetRotation = TempleExitTransition.TargetRotation,
                },
                trialProgress = TrialProgress is null ? null : new
                {
                    TrialProgress.RouteSha256,
                    TrialProgress.Stage,
                    TrialProgress.GlobalVariable10,
                    TrialProgress.CameronLocalVariable12,
                    TrialProgress.CameronLocalVariable13,
                    TrialProgress.CameronMapVariable20,
                    TrialProgress.CameronDialogueSelections,
                    TrialProgress.CameronTile,
                    TrialProgress.CameronVisible,
                    TrialProgress.CameronDoorOpened,
                    TrialProgress.CameronDoorUnlocked,
                    TrialProgress.KlintGateTile,
                    TrialProgress.KlintAlive,
                    TrialProgress.VillageRouteCompleted,
                    TrialProgress.VillageCurrentTile,
                    TrialProgress.VillageFirstActionApplied,
                },
            };
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            File.Move(temporary, Path, true);
            return this with { Sha256 = FileSha256(Path) };
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static Fo2CharacterStartSaveState Load(
        string configuredPath,
        Fo2CharacterStartCatalog characterStart,
        Fo2ArroyoCavesPresentationCatalog arroyo,
        Fo2TemplePresentationCatalog temple,
        Fo2TempleTransitionCatalog transitions,
        Fo2ArroyoPlayerProfile runtimeProfile,
        Fo2ArroyoTrialRouteContract? trialRoute = null)
    {
        var path = ResolvePath(configuredPath);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var schema = RequiredString(root, "schema");
        var legacy = schema == LegacySchema;
        var previous = schema == PreviousSchema;
        var route = schema == RouteSchema;
        var confrontation = schema == ConfrontationSchema;
        if (schema != Schema && schema != PriorSchema && schema != VersionNineSchema &&
                schema != ColorAppearanceSchema &&
                schema != ProceduralAppearanceSchema &&
                schema != FaceAppearanceSchema &&
                schema != AppearanceSchema &&
                schema != ConfrontationSchema && schema != RouteSchema &&
                schema != PreviousSchema && schema != LegacySchema ||
            RequiredString(root, "campaign") != "Fallout2" ||
            RequiredString(root, "routeMode") !=
                (legacy ? LegacyRouteMode : previous ? PreviousRouteMode : RouteMode) ||
            RequiredString(root, "sourceProfileId") != characterStart.SourceProfileId ||
            characterStart.SourceProfileId != arroyo.SourceProfileId ||
            temple.SourceProfileId != arroyo.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 save does not match the active owned source profile.");

        var savedCharacter = root.GetProperty("character");
        var mode = legacy
            ? Fo2CharacterSelection.PremadeMode
            : RequiredString(savedCharacter, "Mode");
        var sourceId = legacy
            ? RequiredString(savedCharacter, "Id")
            : RequiredString(savedCharacter, "SourceId");
        var source = characterStart.Characters.SingleOrDefault(row => row.Id == sourceId) ??
            throw new InvalidOperationException(
                $"Fallout 2 save names an unavailable owned source basis: {sourceId}");
        if (RequiredString(savedCharacter, legacy ? "Role" : "SourceRole") != source.Role ||
            RequiredString(savedCharacter, "GcdSha256") != source.GcdSha256 ||
            RequiredString(savedCharacter, "BioSha256") != source.BioSha256)
            throw new InvalidOperationException(
                "Fallout 2 saved character source basis differs from its owned GCD/BIO state.");
        var profile = new Fo2CharacterProfile(
            RequiredString(savedCharacter, "Name"),
            savedCharacter.GetProperty("Age").GetInt32(),
            RequiredString(savedCharacter, "Sex"),
            ReadInts(savedCharacter.GetProperty("special")),
            ReadStrings(savedCharacter.GetProperty("taggedSkills")),
            ReadStrings(savedCharacter.GetProperty("traits")));
        var provisionalCharacter = new Fo2CharacterSelection(mode, source, profile);
        var character = provisionalCharacter with
        {
            AppearanceState = ReadAppearance(root, schema, provisionalCharacter),
        };
        character.Validate(characterStart);
        if (RequiredString(savedCharacter, "Id") != character.Id ||
            RequiredString(savedCharacter, "Role") != character.Role)
            throw new InvalidOperationException(
                "Fallout 2 saved character route identity drifted.");
        var world = root.GetProperty("world");
        var mapIndex = world.GetProperty("mapIndex").GetInt32();
        var elevation = world.GetProperty("elevation").GetInt32();
        var arrivalTile = world.GetProperty("arrivalTile").GetInt32();
        var currentTile = world.GetProperty("currentTile").GetInt32();
        var rotation = world.GetProperty("rotation").GetInt32();
        var position = ReadVector(world.GetProperty("position"));
        var lastTransition = ReadLastTransition(root, schema, arroyo);
        var templeConfrontation = ReadTempleConfrontation(
            root,
            schema,
            mapIndex,
            character,
            temple);
        var trialProgress = ReadTrialProgress(root, schema, trialRoute);
        var templeExitTransition = ReadTempleExitTransition(
            root,
            schema,
            mapIndex,
            currentTile,
            transitions,
            trialProgress,
            trialRoute);
        var tileInRange = currentTile is >= 0 and < Fo1HexMath.Width * Fo1HexMath.Height;
        var mapIdentityValid = mapIndex switch
        {
            Fo2ArroyoCavesPresentationCatalog.MapIndex =>
                elevation == Fo2ArroyoCavesPresentationCatalog.Elevation &&
                arrivalTile == arroyo.ArrivalTile &&
                RequiredString(world, "mapSha256") == arroyo.MapSha256 &&
                RequiredString(world, "walkMaskSha256") == arroyo.WalkMaskSha256 &&
                tileInRange && arroyo.Walkable[currentTile] && lastTransition is null,
            Fo2TemplePresentationCatalog.MapIndex =>
                (schema == Schema || schema == PriorSchema || schema == VersionNineSchema ||
                    schema == ColorAppearanceSchema ||
                    schema == ProceduralAppearanceSchema ||
                    schema == FaceAppearanceSchema ||
                    schema == AppearanceSchema || confrontation || route) &&
                elevation == arroyo.LiveExit.TargetElevation &&
                arrivalTile == arroyo.LiveExit.TargetTile &&
                RequiredString(world, "mapSha256") == temple.MapSha256 &&
                lastTransition == arroyo.LiveExit,
            4 => schema == Schema && trialProgress is not null && trialRoute is not null &&
                elevation == trialRoute.VillageArrival.Elevation &&
                arrivalTile == trialRoute.VillageArrival.ArrivalTile &&
                RequiredString(world, "mapSha256") ==
                    trialRoute.VillageArrival.MapSha256 &&
                RequiredString(world, "walkMaskSha256") ==
                    trialRoute.VillageArrival.WalkMaskSha256 &&
                currentTile == trialRoute.VillageArrival.FirstActionToTile &&
                lastTransition == arroyo.LiveExit,
            _ => false,
        };
        if (!tileInRange ||
            !mapIdentityValid ||
            rotation is < 0 or >= Fo1HexMath.DirectionCount ||
            Fo1HexMath.NearestTile(new Vector3(position.X, 0.0f, position.Z)) != currentTile ||
            MathF.Abs(position.Y - runtimeProfile.SpawnCenterHeightMeters) >
                runtimeProfile.FloorSnapLengthMeters)
            throw new InvalidOperationException(
                "Fallout 2 saved player state is outside the admitted Arroyo/Temple route.");

        var runtime = root.GetProperty("runtime");
        var motionMode = RequiredString(runtime, "motionMode");
        var blockedMovementMode = RequiredString(runtime, "blockedMovementMode");
        var presentationMode = RequiredString(runtime, "presentationMode");
        if (RequiredString(runtime, "profileId") != runtimeProfile.Id ||
            RequiredString(runtime, "profileSha256") != runtimeProfile.Sha256 ||
            motionMode != CharacterBody3D.MotionModeEnum.Grounded.ToString() ||
            blockedMovementMode != runtimeProfile.BlockedMovementMode ||
            presentationMode != runtimeProfile.PlayerPresentationMode)
            throw new InvalidOperationException(
                "Fallout 2 saved runtime mode differs from the admitted Map 3 profile.");

        return new Fo2CharacterStartSaveState(
            path,
            FileSha256(path),
            characterStart.SourceProfileId,
            character,
            runtimeProfile.Id,
            runtimeProfile.Sha256,
            RequiredString(world, "mapSha256"),
            RequiredString(world, "walkMaskSha256"),
            mapIndex,
            elevation,
            arrivalTile,
            currentTile,
            rotation,
            position,
            motionMode,
            blockedMovementMode,
            presentationMode,
            lastTransition,
            templeConfrontation,
            templeExitTransition,
            trialProgress);
    }

    private static Fo2TempleAppliedTransition? ReadTempleExitTransition(
        JsonElement root,
        string schema,
        int mapIndex,
        int currentTile,
        Fo2TempleTransitionCatalog transitions,
        Fo2ArroyoTrialProgressState? trialProgress,
        Fo2ArroyoTrialRouteContract? trialRoute)
    {
        if (schema != Schema)
        {
            if (schema == PriorSchema &&
                root.TryGetProperty("templeExitTransition", out var priorExit) &&
                priorExit.ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException(
                    "Fallout 2 v10 post-Klint exit saves used the rejected guardian shortcut.");
            return null;
        }
        var value = root.GetProperty("templeExitTransition");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        var applied = new Fo2TempleAppliedTransition(
            value.GetProperty("exitSerial").GetInt32(),
            value.GetProperty("sourceMapIndex").GetInt32(),
            RequiredString(value, "sourceMapSha256"),
            value.GetProperty("sourceTile").GetInt32(),
            value.GetProperty("targetMapIndex").GetInt32(),
            RequiredString(value, "targetMapSha256"),
            RequiredString(value, "targetMapName"),
            value.GetProperty("targetTile").GetInt32(),
            value.GetProperty("targetElevation").GetInt32(),
            value.GetProperty("targetRotation").GetInt32());
        ValidateTempleExitTransition(
            applied,
            transitions,
            mapIndex,
            currentTile,
            trialProgress,
            trialRoute);
        return applied;
    }

    private static void ValidateTempleExitTransition(
        Fo2TempleAppliedTransition? applied,
        Fo2TempleTransitionCatalog transitions,
        int mapIndex,
        int currentTile,
        Fo2ArroyoTrialProgressState? trialProgress,
        Fo2ArroyoTrialRouteContract? trialRoute)
    {
        if (applied is null)
            return;
        var exit = transitions.Exits.SingleOrDefault(row => row.Serial == applied.ExitSerial);
        if (exit is null ||
            !transitions.DestinationMaps.TryGetValue(exit.TargetMapIndex, out var destination) ||
            applied != new Fo2TempleAppliedTransition(
                exit.Serial,
                Fo2TemplePresentationCatalog.MapIndex,
                transitions.SourceMapSha256,
                exit.Tile,
                exit.TargetMapIndex,
                destination.Sha256,
                destination.MapName,
                exit.TargetTile,
                exit.TargetElevation,
                exit.TargetRotation) ||
            mapIndex != trialRoute?.VillageArrival.MapIndex ||
            currentTile != trialRoute?.VillageArrival.FirstActionToTile ||
            applied.TargetMapIndex != 4 || trialProgress is null || trialRoute is null ||
            trialProgress.Stage != Fo2ArroyoTrialProgressState.VillageFirstActionStage ||
            !trialProgress.VillageRouteCompleted || trialProgress.GlobalVariable10 !=
                trialRoute.KlintGate.RequiredGlobalVariable10 ||
            !trialProgress.VillageFirstActionApplied ||
            exit.Serial != trialRoute.Village.ExitSerial)
            throw new InvalidOperationException(
                "Fallout 2 saved post-trial exit does not match Cameron, ACKlint, and MAP state.");
    }

    private static Fo2ArroyoTrialProgressState? ReadTrialProgress(
        JsonElement root,
        string schema,
        Fo2ArroyoTrialRouteContract? trialRoute)
    {
        if (schema != Schema)
            return null;
        var value = root.GetProperty("trialProgress");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        var contract = trialRoute ?? throw new InvalidOperationException(
            "Fallout 2 trial save was loaded without its source route contract.");
        var state = new Fo2ArroyoTrialProgressState(
            RequiredString(value, "RouteSha256"),
            RequiredString(value, "Stage"),
            value.GetProperty("GlobalVariable10").GetInt32(),
            value.GetProperty("CameronLocalVariable12").GetInt32(),
            value.GetProperty("CameronLocalVariable13").GetInt32(),
            value.GetProperty("CameronMapVariable20").GetInt32(),
            value.GetProperty("CameronDialogueSelections").GetInt32(),
            value.GetProperty("CameronTile").GetInt32(),
            value.GetProperty("CameronVisible").GetBoolean(),
            value.GetProperty("CameronDoorOpened").GetBoolean(),
            value.GetProperty("CameronDoorUnlocked").GetBoolean(),
            value.GetProperty("KlintGateTile").GetInt32(),
            value.GetProperty("KlintAlive").GetBoolean(),
            value.GetProperty("VillageRouteCompleted").GetBoolean(),
            value.GetProperty("VillageCurrentTile").GetInt32(),
            value.GetProperty("VillageFirstActionApplied").GetBoolean());
        state.Validate(contract);
        return state;
    }

    private static Fo2ArroyoExitTransition? ReadLastTransition(
        JsonElement root,
        string schema,
        Fo2ArroyoCavesPresentationCatalog arroyo)
    {
        if (schema != Schema && schema != PriorSchema && schema != VersionNineSchema &&
            schema != ColorAppearanceSchema &&
            schema != ProceduralAppearanceSchema &&
            schema != FaceAppearanceSchema &&
            schema != AppearanceSchema &&
            schema != ConfrontationSchema && schema != RouteSchema)
            return null;
        var value = root.GetProperty("lastTransition");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        var expected = arroyo.LiveExit;
        if (value.GetProperty("sourceMapIndex").GetInt32() != expected.SourceMapIndex ||
            RequiredString(value, "sourceMapSha256") != expected.SourceMapSha256 ||
            value.GetProperty("sourceTile").GetInt32() != expected.SourceTile ||
            value.GetProperty("sourceElevation").GetInt32() != expected.SourceElevation ||
            value.GetProperty("exitSerial").GetInt32() != expected.ExitSerial ||
            RequiredString(value, "exitFid") != expected.ExitFid ||
            RequiredString(value, "exitPid") != expected.ExitPid ||
            RequiredString(value, "sourcePathSha256") != expected.SourcePathSha256 ||
            value.GetProperty("targetMapIndex").GetInt32() != expected.TargetMapIndex ||
            RequiredString(value, "targetLogicalPath") != expected.TargetLogicalPath ||
            RequiredString(value, "targetMapSha256") != expected.TargetMapSha256 ||
            value.GetProperty("targetTile").GetInt32() != expected.TargetTile ||
            value.GetProperty("targetElevation").GetInt32() != expected.TargetElevation ||
            value.GetProperty("targetRotation").GetInt32() != expected.TargetRotation)
            throw new InvalidOperationException(
                "Fallout 2 saved transition differs from the owned exit-grid contract.");
        return expected;
    }

    private static Fo2TempleConfrontationState? ReadTempleConfrontation(
        JsonElement root,
        string schema,
        int mapIndex,
        Fo2CharacterSelection character,
        Fo2TemplePresentationCatalog temple)
    {
        if (schema != Schema && schema != PriorSchema && schema != VersionNineSchema &&
            schema != ColorAppearanceSchema &&
            schema != ProceduralAppearanceSchema &&
            schema != FaceAppearanceSchema &&
            schema != AppearanceSchema && schema != ConfrontationSchema)
            return null;
        var value = root.GetProperty("templeConfrontation");
        if (mapIndex == Fo2ArroyoCavesPresentationCatalog.MapIndex)
        {
            if (value.ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo save contains Temple confrontation state.");
            return null;
        }
        if (mapIndex is not (Fo2TemplePresentationCatalog.MapIndex or 4) ||
            value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                "Fallout 2 Temple save is missing confrontation state.");
        var state = new Fo2TempleConfrontationState(
            value.GetProperty("targetHitPoints").GetInt32(),
            value.GetProperty("playerActionPoints").GetInt32(),
            value.GetProperty("combatActive").GetBoolean(),
            value.GetProperty("spearLooted").GetBoolean(),
            value.TryGetProperty("spearEquipped", out var equipped) &&
                equipped.GetBoolean());
        state.Validate(
            temple.Confrontation,
            Fo2TempleConfrontationRuntime.MaximumActionPoints(character));
        return state;
    }

    private static Fo2CharacterAppearanceContract ReadAppearance(
        JsonElement root,
        string schema,
        Fo2CharacterSelection character)
    {
        if (schema != Schema && schema != PriorSchema && schema != VersionNineSchema)
        {
            if (character.Mode == Fo2CharacterSelection.PremadeMode)
                return Fo2CharacterAppearanceContract.FromSelection(character);
            var recipe = Fo2ProceduralAppearanceCatalog.Load();
            var hasFace = schema == ColorAppearanceSchema ||
                schema == ProceduralAppearanceSchema ||
                schema == FaceAppearanceSchema;
            var priorAppearance = hasFace ? root.GetProperty("appearance") : default;
            var faceShapeId = hasFace
                ? RequiredString(root.GetProperty("appearance"), "FaceShapeId")
                : recipe.DefaultFaceShapeId;
            var hasHairAndSkin = schema == ColorAppearanceSchema ||
                schema == ProceduralAppearanceSchema;
            var hairStyleId = hasHairAndSkin
                ? RequiredString(priorAppearance, "HairStyleId")
                : recipe.DefaultHairStyleId;
            var skinToneId = hasHairAndSkin
                ? RequiredString(priorAppearance, "SkinToneId")
                : recipe.DefaultSkinToneId;
            var hairColorId = schema == ColorAppearanceSchema
                ? RequiredString(priorAppearance, "HairColorId")
                : recipe.DefaultHairColorId;
            var eyeColorId = schema == ColorAppearanceSchema
                ? RequiredString(priorAppearance, "EyeColorId")
                : recipe.DefaultEyeColorId;
            return Fo2ProceduralPortrait.Commit(
                character.Source,
                character.Profile.Sex,
                faceShapeId,
                hairStyleId,
                skinToneId,
                hairColorId,
                eyeColorId,
                recipe.DefaultBrowStyleId,
                recipe.DefaultNoseStyleId,
                recipe.DefaultMouthStyleId);
        }
        var value = root.GetProperty("appearance");
        var appearance = new Fo2CharacterAppearanceContract(
            RequiredString(value, "Schema"),
            RequiredString(value, "BasisPremadeId"),
            RequiredString(value, "SourcePanelLogicalPath"),
            RequiredString(value, "SourcePanelSha256"),
            RequiredString(value, "LocalPanelPngSha256"),
            RequiredString(value, "PreviewMode"),
            RequiredString(value, "PortraitState"),
            value.GetProperty("CustomFaceEdited").GetBoolean(),
            value.GetProperty("CustomPortraitGenerated").GetBoolean(),
            RequiredString(value, "FaceShapeId"),
            RequiredString(value, "HairStyleId"),
            RequiredString(value, "SkinToneId"),
            RequiredString(value, "HairColorId"),
            RequiredString(value, "EyeColorId"),
            RequiredString(value, "BrowStyleId"),
            RequiredString(value, "NoseStyleId"),
            RequiredString(value, "MouthStyleId"),
            RequiredString(value, "PortraitGeneratorId"),
            RequiredString(value, "AppearanceRecipeId"),
            value.GetProperty("AppearanceRecipeSha256").GetString() ?? "",
            value.GetProperty("GeneratedPortraitPath").GetString() ?? "",
            value.GetProperty("GeneratedPortraitSha256").GetString() ?? "",
            value.GetProperty("GeneratedPortraitWidth").GetInt32(),
            value.GetProperty("GeneratedPortraitHeight").GetInt32());
        appearance.Validate(character);
        return appearance;
    }

    private static string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ArgumentException("Fallout 2 save path is empty.", nameof(configuredPath));
        if (configuredPath.StartsWith("user://", StringComparison.Ordinal))
            return ProjectSettings.GlobalizePath(configuredPath);
        var path = System.IO.Path.GetFullPath(configuredPath);
        var localRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "OpenNV"));
        if (!path.StartsWith(
                localRoot + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 2 saves must stay in the OpenNV user-data sandbox.");
        return path;
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 save string is empty: {property}");
    }

    private static int[] ReadInts(JsonElement source) =>
        source.EnumerateArray().Select(row => row.GetInt32()).ToArray();

    private static string[] ReadStrings(JsonElement source) =>
        source.EnumerateArray().Select(row => row.GetString() ?? "").ToArray();

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout 2 saved player position is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
