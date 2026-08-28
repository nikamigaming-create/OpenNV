using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1RuntimeProfileNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const int PresentationInt64 = 64;
}

internal sealed record Fo1RuntimeProfile(
    string Id,
    string RecipeSha256,
    Fo1RuntimeAuthority Authority,
    Fo1GenerationAdaptationProfile Generation,
    Fo1ScenePresentationProfile Scene,
    Fo1CameraProfile Camera,
    Fo1GameplayAdaptationProfile Gameplay,
    Fo1CombatPresentationProfile CombatPresentation,
    Fo1MobPresentationProfile Mob,
    Fo1CutawayProfile Cutaway,
    Fo1ShowcaseProfile Showcase)
{
    private const string Schema = "opennv-fo1-runtime-profile-recipe/v1";

    internal static Fo1RuntimeProfile Parse(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != Schema)
            throw new InvalidOperationException("Unexpected Fallout runtime-profile schema.");
        var id = RequiredString(source, "id");
        var recipeSha256 = RequiredString(source, "recipeSha256");
        if (recipeSha256.Length != Fo1RuntimeProfileNumericContracts.PresentationInt64)
            throw new InvalidOperationException("Fallout runtime-profile recipe hash is invalid.");

        var authority = source.GetProperty("authority");
        var generation = source.GetProperty("generationAdaptation");
        var scene = source.GetProperty("scenePresentation");
        var sourceFloor = scene.GetProperty("sourceFloor");
        var footprint = scene.GetProperty("presentationFootprint");
        var overlay = scene.GetProperty("hexOverlay");
        var sprites = scene.GetProperty("sourceSprites");
        var door = scene.GetProperty("door");
        var atmosphere = scene.GetProperty("atmosphere");
        var directional = atmosphere.GetProperty("directionalLight");

        var practicalLights = atmosphere.GetProperty("practicalLights").EnumerateArray()
            .Select(row => new Fo1PracticalLightProfile(
                RequiredString(row, "id"),
                ReadAnchor(row),
                Finite(row, "forwardMeters"),
                Finite(row, "lateralMeters"),
                Positive(row, "heightMeters"),
                Color(row, "color"),
                Positive(row, "energy"),
                Positive(row, "rangeMeters"),
                Positive(row, "attenuation")))
            .ToArray();
        if (practicalLights.Length == 0 || practicalLights.Select(row => row.Id).Distinct().Count() != practicalLights.Length)
            throw new InvalidOperationException("Fallout practical-light profile is empty or has duplicate IDs.");
        var localFogVolumes = atmosphere.GetProperty("localFogVolumes").EnumerateArray()
            .Select(row => new Fo1LocalFogProfile(
                RequiredString(row, "id"),
                ReadAnchor(row),
                Finite(row, "forwardMeters"),
                Finite(row, "lateralMeters"),
                Positive(row, "heightMeters"),
                Vector3(row, "sizeMeters", positive: true),
                Color(row, "color"),
                Positive(row, "density"),
                NonNegative(row, "emissionScale"),
                Positive(row, "heightFalloff"),
                Unit(row, "edgeFade")))
            .ToArray();
        if (localFogVolumes.Length == 0 || localFogVolumes.Select(row => row.Id).Distinct().Count() != localFogVolumes.Length)
            throw new InvalidOperationException("Fallout local-fog profile is empty or has duplicate IDs.");

        var camera = source.GetProperty("camera");
        var tactical = camera.GetProperty("tactical");
        var combatFraming = tactical.GetProperty("combatFraming");
        var entryFraming = tactical.GetProperty("entryFraming");
        var shoulder = camera.GetProperty("shoulder");
        var firstPerson = camera.GetProperty("firstPerson");
        var gameplay = source.GetProperty("gameplayAdaptation");
        var combatPresentation = source.GetProperty("combatPresentation");
        var mob = source.GetProperty("mobPresentation");
        var sourceHighlight = mob.GetProperty("sourceHighlight");
        var creatureHighlight = mob.GetProperty("creatureHighlight");
        var hostileMarker = mob.GetProperty("hostileMarker");
        var hostileBeacon = mob.GetProperty("hostileBeacon");
        var healthLabel = mob.GetProperty("healthLabel");
        var readability = mob.GetProperty("readability");
        var animation = mob.GetProperty("animation");
        var cutaway = source.GetProperty("cutaway");
        var showcase = source.GetProperty("showcase");
        EnsureIncreasing(
            generation,
            "obstacleMinimumHeightMeters",
            "obstacleMaximumHeightMeters");
        EnsureIncreasing(
            generation,
            "obstacleMinimumRadiusMeters",
            "obstacleMaximumRadiusMeters");
        EnsureIncreasing(tactical, "minimumSizeMeters", "maximumSizeMeters");
        EnsureIncreasing(tactical, "minimumPitchDegrees", "maximumPitchDegrees");
        EnsureIncreasing(tactical, "nearClipMeters", "farClipMeters");
        EnsureIncreasing(combatFraming, "minimumSizeMeters", "maximumSizeMeters");
        EnsureIncreasing(entryFraming, "minimumSizeMeters", "maximumSizeMeters");
        EnsureIncreasing(shoulder, "minimumPitchDegrees", "maximumPitchDegrees");
        EnsureIncreasing(shoulder, "minimumDistanceMeters", "maximumDistanceMeters");
        EnsureWithin(
            shoulder,
            "defaultDistanceMeters",
            "minimumDistanceMeters",
            "maximumDistanceMeters");
        EnsureIncreasing(firstPerson, "minimumPitchDegrees", "maximumPitchDegrees");
        EnsureIncreasing(hostileMarker, "innerRadiusMeters", "outerRadiusMeters");
        EnsureIncreasing(
            gameplay,
            "tacticalMinimumHitChancePercent",
            "tacticalMaximumHitChancePercent");

        return new Fo1RuntimeProfile(
            id,
            recipeSha256,
            new Fo1RuntimeAuthority(
                RequiredString(authority, "fallout1"),
                RequiredString(authority, "falloutNewVegas"),
                RequiredString(authority, "openNvAdaptation"),
                RequiredString(authority, "proofOnly")),
            new Fo1GenerationAdaptationProfile(
                PositiveInt(generation, "unprojectedFloorTextureSizePixels"),
                Positive(generation, "obstacleMinimumHeightMeters"),
                Positive(generation, "obstacleMaximumHeightMeters"),
                Positive(generation, "obstacleMinimumRadiusMeters"),
                Positive(generation, "obstacleMaximumRadiusMeters"),
                Positive(generation, "proceduralBoundaryHeightMeters"),
                Finite(generation, "staticWorldSpriteYawDegrees"),
                PositiveInt(generation, "rockSerialYawMultiplierDegrees"),
                NonNegative(generation, "corridorClosurePaddingMeters"),
                Finite(generation, "corpseYawOffsetDegrees"),
                Finite(generation, "corpsePitchDegrees")),
            new Fo1ScenePresentationProfile(
                new Fo1SourceFloorProfile(
                    Color(sourceFloor, "albedoColor"),
                    Finite(sourceFloor, "yOffsetMeters")),
                new Fo1PresentationFootprintProfile(
                    NonNegative(footprint, "obstaclePaddingMeters"),
                    Positive(footprint, "vaultBehindDoorMeters"),
                    NonNegative(footprint, "vaultCavewardMeters"),
                    Positive(footprint, "vaultHalfWidthMeters")),
                new Fo1HexOverlayProfile(
                    Positive(overlay, "edgeWidthMeters"),
                    NonNegative(overlay, "yOffsetMeters"),
                    Color(overlay, "albedoColor"),
                    Color(overlay, "emissionColor"),
                    NonNegative(overlay, "emissionEnergy")),
                new Fo1SourceSpriteProfile(
                    NonNegative(sprites, "groundAnchorMeters"),
                    Positive(sprites, "pixelsPerMeter")),
                new Fo1DoorPresentationProfile(
                    NonNegative(door, "sourceFrameDepthOffsetMeters"),
                    NonNegative(door, "identityLabelHeightMeters"),
                    Positive(door, "identityLabelPixelSize"),
                    Color(door, "identityLabelColor"),
                    RequiredString(door, "doorNumber"),
                    PositiveInt(door, "doorNumberFontSize"),
                    Positive(door, "doorNumberPixelSize"),
                    Color(door, "doorNumberColor"),
                    NonNegative(door, "doorNumberCavewardOffsetMeters"),
                    Positive(door, "corridorNumberBehindDoorMeters"),
                    Positive(door, "corridorNumberHeightMeters"),
                    PositiveInt(door, "corridorNumberFontSize"),
                    Positive(door, "corridorNumberPixelSize"),
                    Color(door, "corridorNumberColor"),
                    Positive(door, "corridorLightBehindDoorMeters"),
                    Positive(door, "corridorLightHeightMeters"),
                    Color(door, "corridorLightColor"),
                    Positive(door, "corridorLightEnergy"),
                    Positive(door, "corridorLightRangeMeters"),
                    Positive(door, "corridorLightAttenuation")),
                new Fo1AtmosphereProfile(
                    Color(atmosphere, "backgroundColor"),
                    Color(atmosphere, "ambientColor"),
                    NonNegative(atmosphere, "ambientEnergy"),
                    Positive(atmosphere, "tonemapExposure"),
                    Color(atmosphere, "fogColor"),
                    NonNegative(atmosphere, "fogLightEnergy"),
                    NonNegative(atmosphere, "fogDensity"),
                    Unit(atmosphere, "fogAerialPerspective"),
                    Unit(atmosphere, "fogSkyAffect"),
                    NonNegative(atmosphere, "volumetricFogDensity"),
                    Color(atmosphere, "volumetricFogAlbedo"),
                    Color(atmosphere, "volumetricFogEmission"),
                    NonNegative(atmosphere, "volumetricFogEmissionEnergy"),
                    Positive(atmosphere, "volumetricFogLengthMeters"),
                    Positive(atmosphere, "volumetricFogDetailSpread"),
                    Unit(atmosphere, "volumetricFogAmbientInject"),
                    Unit(atmosphere, "volumetricFogSkyAffect"),
                    new Fo1DirectionalLightProfile(
                        Vector3(directional, "rotationDegrees"),
                        Color(directional, "color"),
                        Positive(directional, "energy")),
                    practicalLights,
                    localFogVolumes)),
            new Fo1CameraProfile(
                Positive(camera, "smoothingPerSecond"),
                new Fo1TacticalCameraProfile(
                    Positive(tactical, "homeSizeMeters"),
                    Finite(tactical, "homeYawDegrees"),
                    Finite(tactical, "homePitchDegrees"),
                    Positive(tactical, "minimumSizeMeters"),
                    Positive(tactical, "maximumSizeMeters"),
                    Finite(tactical, "minimumPitchDegrees"),
                    Finite(tactical, "maximumPitchDegrees"),
                    Positive(tactical, "keyboardPanMetersPerSecond"),
                    Positive(tactical, "orbitRadiansPerPixel"),
                    Positive(tactical, "keyboardYawStepDegrees"),
                    NonNegative(tactical, "edgeMarginPixels"),
                    Positive(tactical, "nearClipMeters"),
                    Positive(tactical, "farClipMeters"),
                    Positive(tactical, "minimumCameraDistanceMeters"),
                    Positive(tactical, "homeDistanceScale"),
                    Fraction(tactical, "cursorZoomFactor"),
                    Positive(tactical, "playerFocusMaximumSizeMeters"),
                    Positive(tactical, "targetFocusMaximumSizeMeters"),
                    Positive(tactical, "panReferenceSizeMeters"),
                    Positive(tactical, "fastPanMultiplier"),
                    NonNegative(tactical, "guiExclusionMinimumX"),
                    NonNegative(tactical, "guiExclusionBottomPixels"),
                    Color(tactical, "fillLightColor"),
                    NonNegative(tactical, "fillLightEnergy"),
                    ReadPairFraming(combatFraming),
                    ReadPairFraming(entryFraming)),
                new Fo1ShoulderCameraProfile(
                    Finite(shoulder, "minimumPitchDegrees"),
                    Finite(shoulder, "maximumPitchDegrees"),
                    Positive(shoulder, "minimumDistanceMeters"),
                    Positive(shoulder, "maximumDistanceMeters"),
                    Positive(shoulder, "defaultDistanceMeters"),
                    Positive(shoulder, "rigHeightMeters"),
                    Finite(shoulder, "cameraLateralOffsetMeters"),
                    Finite(shoulder, "cameraVerticalOffsetMeters"),
                    Positive(shoulder, "fovDegrees"),
                    Positive(shoulder, "nearClipMeters"),
                    Finite(shoulder, "initialPitchDegrees"),
                    Fraction(shoulder, "minimumMovementAlignment")),
                new Fo1FirstPersonCameraProfile(
                    Finite(firstPerson, "minimumPitchDegrees"),
                    Finite(firstPerson, "maximumPitchDegrees"),
                    Positive(firstPerson, "eyeHeightMeters"),
                    Positive(firstPerson, "fovDegrees"),
                    Positive(firstPerson, "moveSpeedMetersPerSecond"),
                    Positive(firstPerson, "nearClipMeters"),
                    Finite(firstPerson, "initialPitchDegrees"))),
            new Fo1GameplayAdaptationProfile(
                Positive(gameplay, "tacticalMoveSpeedMetersPerSecond"),
                Positive(gameplay, "tacticalArrivalToleranceMeters"),
                PositiveInt(gameplay, "tacticalMoveActionPointCost"),
                Positive(gameplay, "firstPersonMaximumSubstepMeters"),
                Positive(gameplay, "firstPersonShotCooldownSeconds"),
                Positive(gameplay, "firstPersonMeleeCooldownSeconds"),
                Positive(gameplay, "firstPersonMeleeReachMeters"),
                Positive(gameplay, "firstPersonMeleeHitRadiusMeters"),
                Positive(gameplay, "firstPersonMinimumRangeMeters"),
                Positive(gameplay, "firstPersonTargetHeightMeters"),
                NonNegative(gameplay, "firstPersonMinimumForwardMeters"),
                Positive(gameplay, "firstPersonHitRadiusMeters"),
                Positive(gameplay, "firstPersonMetersPerWeaponRangeHex"),
                PositiveInt(gameplay, "tacticalMinimumHitChancePercent"),
                PositiveInt(gameplay, "tacticalMaximumHitChancePercent"),
                PositiveInt(gameplay, "rangedPerceptionRangeMultiplier"),
                PositiveInt(gameplay, "rangedPenaltyPerExcessHexPercent"),
                PositiveInt(gameplay, "strengthPenaltyPerPointPercent"),
                PositiveInt(gameplay, "reloadActionPointCost"),
                PositiveInt(gameplay, "deterministicDamageRollStride"),
                PositiveInt(gameplay, "ratMovementLimitHexes"),
                PositiveInt(gameplay, "ratAttackRangeHexes"),
                PositiveInt(gameplay, "minimumDamage")),
            new Fo1CombatPresentationProfile(
                Positive(combatPresentation, "tracerRadiusMeters"),
                Positive(combatPresentation, "tracerLifetimeSeconds"),
                Color(combatPresentation, "tracerColor"),
                PositiveInt(combatPresentation, "meshRadialSegments"),
                PositiveInt(combatPresentation, "impactRings"),
                Positive(combatPresentation, "tracerEmissionEnergy"),
                NonNegative(combatPresentation, "impactEmissionEnergy"),
                Fraction(combatPresentation, "materialRoughness"),
                Positive(combatPresentation, "impactRadiusMeters"),
                Positive(combatPresentation, "impactLifetimeSeconds"),
                Color(combatPresentation, "impactColor"),
                Positive(combatPresentation, "tacticalMissOffsetMeters"),
                PositiveInt(combatPresentation, "ricochetEveryImpacts"),
                Positive(combatPresentation, "ricochetLengthMeters"),
                Vector3(combatPresentation, "ricochetDirection"),
                Color(combatPresentation, "ricochetColor"),
                Positive(combatPresentation, "casingLifetimeSeconds"),
                Positive(combatPresentation, "casingMassKilograms"),
                Positive(combatPresentation, "casingCollisionRadiusMeters"),
                PositiveInt(combatPresentation, "casingCollisionLayer"),
                Finite(combatPresentation, "casingGroundHeightMeters"),
                Positive(combatPresentation, "casingGroundHalfExtentMeters"),
                Positive(combatPresentation, "casingGroundThicknessMeters"),
                Fraction(combatPresentation, "casingBounce"),
                Fraction(combatPresentation, "casingFriction"),
                Vector3(combatPresentation, "casingAngularVelocityRadiansPerSecond"),
                Positive(combatPresentation, "casingEjectionSpeedMetersPerSecond"),
                Positive(combatPresentation, "casingUpwardSpeedMetersPerSecond"),
                Finite(combatPresentation, "fpsCasingRightMeters"),
                Finite(combatPresentation, "fpsCasingDownMeters"),
                Finite(combatPresentation, "fpsCasingForwardMeters"),
                Positive(combatPresentation, "meleeSweepRadiusMeters"),
                Positive(combatPresentation, "meleeSweepLifetimeSeconds"),
                Color(combatPresentation, "meleeSweepColor"),
                Positive(combatPresentation, "audioUnitSizeMeters"),
                Positive(combatPresentation, "audioMaximumDistanceMeters")),
            new Fo1MobPresentationProfile(
                Positive(mob, "sourceSpriteScale"),
                Positive(mob, "selectedSourceSpriteScale"),
                Positive(mob, "selectedCreatureScale"),
                Positive(mob, "rotationDegreesPerSourceStep"),
                ReadStringArray(mob, "intactHiddenMeshNameFragments"),
                PositiveInt(mob, "expectedIntactHiddenMeshes"),
                new Fo1SourceHighlightProfile(
                    Color(sourceHighlight, "normalColor"),
                    Unit(sourceHighlight, "normalMix"),
                    Color(sourceHighlight, "selectedColor"),
                    Unit(sourceHighlight, "selectedMix"),
                    Color(sourceHighlight, "defeatedColor"),
                    Unit(sourceHighlight, "defeatedMix"),
                    Finite(sourceHighlight, "defeatedRollDegrees")),
                new Fo1CreatureHighlightProfile(
                    Color(creatureHighlight, "normalColor"),
                    NonNegative(creatureHighlight, "normalEnergy"),
                    Color(creatureHighlight, "selectedColor"),
                    NonNegative(creatureHighlight, "selectedEnergy"),
                    Color(creatureHighlight, "defeatedColor"),
                    NonNegative(creatureHighlight, "defeatedEnergy")),
                new Fo1HostileMarkerProfile(
                    Positive(hostileMarker, "innerRadiusMeters"),
                    Positive(hostileMarker, "outerRadiusMeters"),
                    Finite(hostileMarker, "yOffsetMeters"),
                    Color(hostileMarker, "normalColor"),
                    Color(hostileMarker, "normalEmissionColor"),
                    Color(hostileMarker, "selectedColor"),
                    Color(hostileMarker, "selectedEmissionColor"),
                    NonNegative(hostileMarker, "emissionEnergy"),
                    Positive(hostileMarker, "selectedScale")),
                new Fo1HostileBeaconProfile(
                    Positive(hostileBeacon, "topRadiusMeters"),
                    Positive(hostileBeacon, "bottomRadiusMeters"),
                    Positive(hostileBeacon, "heightMeters"),
                    PositiveInt(hostileBeacon, "radialSegments"),
                    Finite(hostileBeacon, "yOffsetMeters"),
                    Color(hostileBeacon, "color"),
                    Color(hostileBeacon, "emissionColor"),
                    NonNegative(hostileBeacon, "emissionEnergy"),
                    Positive(hostileBeacon, "selectedScale")),
                new Fo1HealthLabelProfile(
                    Finite(healthLabel, "yOffsetMeters"),
                    PositiveInt(healthLabel, "fontSize"),
                    Positive(healthLabel, "pixelSize"),
                    Color(healthLabel, "normalColor"),
                    Color(healthLabel, "selectedColor"),
                    Color(healthLabel, "defeatedColor"),
                    PositiveInt(healthLabel, "outlineSize"),
                    Positive(healthLabel, "normalScale"),
                    Positive(healthLabel, "selectedScale")),
                new Fo1MobReadabilityProfile(
                    PositiveInt(readability, "tacticalRangeHexes"),
                    PositiveInt(readability, "perspectiveRangeHexes"),
                    PositiveInt(readability, "beaconRangeHexes")),
                new Fo1MobAnimationProfile(
                    NonNegative(animation, "blendSeconds"),
                    Positive(animation, "moveSeconds"),
                    Positive(animation, "hitHoldSeconds"),
                    Positive(animation, "attackHoldSeconds"),
                    Finite(animation, "deathRollDegrees"),
                    Positive(animation, "deathRollSeconds"))),
            new Fo1CutawayProfile(
                PositiveInt(cutaway, "minimumCandidateInstances"),
                Positive(cutaway, "meltEdgeMeters"),
                Finite(cutaway, "tacticalEnvelopeCutHeightMeters"),
                Positive(cutaway, "playerFocusHeightMeters"),
                Positive(cutaway, "targetFocusHeightMeters"),
                NonNegative(cutaway, "minimumTargetDepthMarginMeters"),
                NonNegative(cutaway, "screenMarginPixels"),
                NonNegative(cutaway, "cameraClearanceMeters"),
                ReadFloatMap(cutaway, "meltRadiusByRoleMeters"),
                ReadFloatMap(cutaway, "tacticalCutHeightByRoleMeters")),
            new Fo1ShowcaseProfile(
                PositiveInt(showcase, "fixedFramesPerSecond"),
                Positive(showcase, "acceleratedOpeningScale"),
                showcase.GetProperty("stageBannerVisible").GetBoolean(),
                PositiveInt(showcase, "openingFadeOutFrames"),
                PositiveInt(showcase, "landingFadeInFrames"),
                PositiveInt(showcase, "landingHoldFrames"),
                PositiveInt(showcase, "vaultLookBackFrames"),
                PositiveInt(showcase, "vaultLookBackHoldFrames"),
                PositiveInt(showcase, "caveLookFrames"),
                PositiveInt(showcase, "caveLookHoldFrames"),
                PositiveInt(showcase, "fpsMoveMaximumHexes"),
                PositiveInt(showcase, "fpsMoveMaximumFrames"),
                PositiveInt(showcase, "fpsMoveHoldFrames"),
                PositiveInt(showcase, "fpsAimFrames"),
                PositiveInt(showcase, "fpsAimHoldFrames"),
                PositiveInt(showcase, "fpsMissAimFrames"),
                PositiveInt(showcase, "fpsMissHoldFrames"),
                PositiveInt(showcase, "fpsShotHoldFrames"),
                PositiveInt(showcase, "fpsKillHoldFrames"),
                PositiveInt(showcase, "fpsPostKillHoldFrames"),
                PositiveInt(showcase, "fpsMeleeApproachTurnFrames"),
                PositiveInt(showcase, "fpsMeleeAimFrames"),
                PositiveInt(showcase, "fpsMeleeAimHoldFrames"),
                PositiveInt(showcase, "fpsMeleeSwingHoldFrames"),
                PositiveInt(showcase, "fpsMeleeKillHoldFrames"),
                PositiveInt(showcase, "reloadHoldFrames"),
                PositiveInt(showcase, "shoulderOrbitFrames"),
                PositiveInt(showcase, "shoulderMoveMaximumHexes"),
                PositiveInt(showcase, "shoulderMoveMaximumFrames"),
                PositiveInt(showcase, "shoulderMoveHoldFrames"),
                PositiveInt(showcase, "modeTransitionHoldFrames"),
                PositiveInt(showcase, "gridHoldFrames"),
                PositiveInt(showcase, "tacticalTourFrames"),
                PositiveInt(showcase, "finalHoldFrames"),
                PositiveInt(showcase, "maximumFpsShots"),
                PositiveInt(showcase, "maximumTacticalAttacks"),
                Positive(showcase, "ratCorpseGroundToleranceMeters"),
                Positive(showcase, "fpsAimTargetHeightMeters"),
                Positive(showcase, "shotCooldownWaitSeconds"),
                Positive(showcase, "tacticalAttackSettleSeconds"),
                PositiveInt(showcase, "tacticalTargetHoldFrames"),
                PositiveInt(showcase, "tacticalFrameHoldFrames"),
                PositiveInt(showcase, "tacticalAttackHoldFrames"),
                PositiveInt(showcase, "tacticalKillHoldFrames"),
                PositiveInt(showcase, "fadeToTacticalOutFrames"),
                PositiveInt(showcase, "fadeToTacticalInFrames")));
    }

    internal object Report() => new
    {
        schema = Schema,
        id = Id,
        recipeSha256 = RecipeSha256,
        authority = Authority,
    };

    private static string ReadAnchor(JsonElement source)
    {
        var value = RequiredString(source, "anchor");
        if (value is not ("door" or "entry"))
            throw new InvalidOperationException($"Unsupported Fallout presentation anchor: {value}");
        return value;
    }

    private static Fo1PairFramingProfile ReadPairFraming(JsonElement source) => new(
        NonNegative(source, "paddingMeters"),
        Positive(source, "minimumSizeMeters"),
        Positive(source, "maximumSizeMeters"),
        NonNegative(source, "reservedHudPixels"),
        NonNegative(source, "focusHeightMeters"));

    private static string RequiredString(JsonElement source, string name)
    {
        var value = source.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Fallout runtime-profile string is empty: {name}");
        return value;
    }

    private static int PositiveInt(JsonElement source, string name)
    {
        var value = source.GetProperty(name).GetInt32();
        if (value <= 0)
            throw new InvalidOperationException($"Fallout runtime-profile integer must be positive: {name}");
        return value;
    }

    private static float Finite(JsonElement source, string name)
    {
        var value = source.GetProperty(name).GetSingle();
        if (!float.IsFinite(value))
            throw new InvalidOperationException($"Fallout runtime-profile number is not finite: {name}");
        return value;
    }

    private static float NonNegative(JsonElement source, string name)
    {
        var value = Finite(source, name);
        if (value < 0.0f)
            throw new InvalidOperationException($"Fallout runtime-profile number must be non-negative: {name}");
        return value;
    }

    private static float Positive(JsonElement source, string name)
    {
        var value = Finite(source, name);
        if (value <= 0.0f)
            throw new InvalidOperationException($"Fallout runtime-profile number must be positive: {name}");
        return value;
    }

    private static float Unit(JsonElement source, string name)
    {
        var value = Finite(source, name);
        if (value is < 0.0f or > 1.0f)
            throw new InvalidOperationException($"Fallout runtime-profile number must be in [0,1]: {name}");
        return value;
    }

    private static float Fraction(JsonElement source, string name)
    {
        var value = Finite(source, name);
        if (value is <= 0.0f or >= 1.0f)
            throw new InvalidOperationException($"Fallout runtime-profile number must be in (0,1): {name}");
        return value;
    }

    private static Color Color(JsonElement source, string name)
    {
        var values = source.GetProperty(name).EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value) || value < 0.0f || value > 4.0f))
            throw new InvalidOperationException($"Fallout runtime-profile color is invalid: {name}");
        return new Color(values[0], values[1], values[2], values[3]);
    }

    private static Vector3 Vector3(JsonElement source, string name, bool positive = false)
    {
        var values = source.GetProperty(name).EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)) ||
            positive && values.Any(value => value <= 0.0f))
            throw new InvalidOperationException($"Fallout runtime-profile vector is invalid: {name}");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static IReadOnlyDictionary<string, float> ReadFloatMap(JsonElement source, string name)
    {
        var result = source.GetProperty(name).EnumerateObject().ToDictionary(
            row => row.Name,
            row => row.Value.GetSingle(),
            StringComparer.Ordinal);
        if (!result.ContainsKey("default") || result.Values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException($"Fallout runtime-profile role map is invalid: {name}");
        return result;
    }

    private static int[] ReadPositiveIntArray(JsonElement source, string name, int expectedCount)
    {
        var result = source.GetProperty(name).EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (result.Length != expectedCount || result.Any(value => value <= 0))
            throw new InvalidOperationException($"Fallout runtime-profile integer array is invalid: {name}");
        return result;
    }

    private static string[] ReadStringArray(JsonElement source, string name)
    {
        var result = source.GetProperty(name).EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        if (result.Length == 0 || result.Any(string.IsNullOrWhiteSpace) ||
            result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
            throw new InvalidOperationException(
                $"Fallout runtime-profile string array is invalid: {name}");
        return result.Select(value => value!).ToArray();
    }

    private static void EnsureIncreasing(JsonElement source, string minimumName, string maximumName)
    {
        if (Finite(source, minimumName) >= Finite(source, maximumName))
            throw new InvalidOperationException(
                $"Fallout runtime-profile range is invalid: {minimumName}/{maximumName}");
    }

    private static void EnsureWithin(
        JsonElement source,
        string valueName,
        string minimumName,
        string maximumName)
    {
        var value = Finite(source, valueName);
        if (value < Finite(source, minimumName) || value > Finite(source, maximumName))
            throw new InvalidOperationException(
                $"Fallout runtime-profile value is outside its range: {valueName}");
    }
}

internal sealed record Fo1RuntimeAuthority(
    string Fallout1,
    string FalloutNewVegas,
    string OpenNvAdaptation,
    string ProofOnly);

internal sealed record Fo1GenerationAdaptationProfile(
    int UnprojectedFloorTextureSizePixels,
    float ObstacleMinimumHeightMeters,
    float ObstacleMaximumHeightMeters,
    float ObstacleMinimumRadiusMeters,
    float ObstacleMaximumRadiusMeters,
    float ProceduralBoundaryHeightMeters,
    float StaticWorldSpriteYawDegrees,
    int RockSerialYawMultiplierDegrees,
    float CorridorClosurePaddingMeters,
    float CorpseYawOffsetDegrees,
    float CorpsePitchDegrees);

internal sealed record Fo1ScenePresentationProfile(
    Fo1SourceFloorProfile SourceFloor,
    Fo1PresentationFootprintProfile PresentationFootprint,
    Fo1HexOverlayProfile HexOverlay,
    Fo1SourceSpriteProfile SourceSprites,
    Fo1DoorPresentationProfile Door,
    Fo1AtmosphereProfile Atmosphere);

internal sealed record Fo1SourceFloorProfile(Color AlbedoColor, float YOffsetMeters);

internal sealed record Fo1PresentationFootprintProfile(
    float ObstaclePaddingMeters,
    float VaultBehindDoorMeters,
    float VaultCavewardMeters,
    float VaultHalfWidthMeters);

internal sealed record Fo1HexOverlayProfile(
    float EdgeWidthMeters,
    float YOffsetMeters,
    Color AlbedoColor,
    Color EmissionColor,
    float EmissionEnergy);

internal sealed record Fo1SourceSpriteProfile(float GroundAnchorMeters, float PixelsPerMeter);

internal sealed record Fo1DoorPresentationProfile(
    float SourceFrameDepthOffsetMeters,
    float IdentityLabelHeightMeters,
    float IdentityLabelPixelSize,
    Color IdentityLabelColor,
    string DoorNumber,
    int DoorNumberFontSize,
    float DoorNumberPixelSize,
    Color DoorNumberColor,
    float DoorNumberCavewardOffsetMeters,
    float CorridorNumberBehindDoorMeters,
    float CorridorNumberHeightMeters,
    int CorridorNumberFontSize,
    float CorridorNumberPixelSize,
    Color CorridorNumberColor,
    float CorridorLightBehindDoorMeters,
    float CorridorLightHeightMeters,
    Color CorridorLightColor,
    float CorridorLightEnergy,
    float CorridorLightRangeMeters,
    float CorridorLightAttenuation);

internal sealed record Fo1AtmosphereProfile(
    Color BackgroundColor,
    Color AmbientColor,
    float AmbientEnergy,
    float TonemapExposure,
    Color FogColor,
    float FogLightEnergy,
    float FogDensity,
    float FogAerialPerspective,
    float FogSkyAffect,
    float VolumetricFogDensity,
    Color VolumetricFogAlbedo,
    Color VolumetricFogEmission,
    float VolumetricFogEmissionEnergy,
    float VolumetricFogLengthMeters,
    float VolumetricFogDetailSpread,
    float VolumetricFogAmbientInject,
    float VolumetricFogSkyAffect,
    Fo1DirectionalLightProfile DirectionalLight,
    IReadOnlyList<Fo1PracticalLightProfile> PracticalLights,
    IReadOnlyList<Fo1LocalFogProfile> LocalFogVolumes);

internal sealed record Fo1DirectionalLightProfile(Vector3 RotationDegrees, Color Color, float Energy);

internal sealed record Fo1PracticalLightProfile(
    string Id,
    string Anchor,
    float ForwardMeters,
    float LateralMeters,
    float HeightMeters,
    Color Color,
    float Energy,
    float RangeMeters,
    float Attenuation);

internal sealed record Fo1LocalFogProfile(
    string Id,
    string Anchor,
    float ForwardMeters,
    float LateralMeters,
    float HeightMeters,
    Vector3 SizeMeters,
    Color Color,
    float Density,
    float EmissionScale,
    float HeightFalloff,
    float EdgeFade);

internal sealed record Fo1CameraProfile(
    float SmoothingPerSecond,
    Fo1TacticalCameraProfile Tactical,
    Fo1ShoulderCameraProfile Shoulder,
    Fo1FirstPersonCameraProfile FirstPerson);

internal sealed record Fo1TacticalCameraProfile(
    float HomeSizeMeters,
    float HomeYawDegrees,
    float HomePitchDegrees,
    float MinimumSizeMeters,
    float MaximumSizeMeters,
    float MinimumPitchDegrees,
    float MaximumPitchDegrees,
    float KeyboardPanMetersPerSecond,
    float OrbitRadiansPerPixel,
    float KeyboardYawStepDegrees,
    float EdgeMarginPixels,
    float NearClipMeters,
    float FarClipMeters,
    float MinimumCameraDistanceMeters,
    float HomeDistanceScale,
    float CursorZoomFactor,
    float PlayerFocusMaximumSizeMeters,
    float TargetFocusMaximumSizeMeters,
    float PanReferenceSizeMeters,
    float FastPanMultiplier,
    float GuiExclusionMinimumX,
    float GuiExclusionBottomPixels,
    Color FillLightColor,
    float FillLightEnergy,
    Fo1PairFramingProfile CombatFraming,
    Fo1PairFramingProfile EntryFraming);

internal sealed record Fo1PairFramingProfile(
    float PaddingMeters,
    float MinimumSizeMeters,
    float MaximumSizeMeters,
    float ReservedHudPixels,
    float FocusHeightMeters);

internal sealed record Fo1ShoulderCameraProfile(
    float MinimumPitchDegrees,
    float MaximumPitchDegrees,
    float MinimumDistanceMeters,
    float MaximumDistanceMeters,
    float DefaultDistanceMeters,
    float RigHeightMeters,
    float CameraLateralOffsetMeters,
    float CameraVerticalOffsetMeters,
    float FovDegrees,
    float NearClipMeters,
    float InitialPitchDegrees,
    float MinimumMovementAlignment);

internal sealed record Fo1FirstPersonCameraProfile(
    float MinimumPitchDegrees,
    float MaximumPitchDegrees,
    float EyeHeightMeters,
    float FovDegrees,
    float MoveSpeedMetersPerSecond,
    float NearClipMeters,
    float InitialPitchDegrees);

internal sealed record Fo1GameplayAdaptationProfile(
    float TacticalMoveSpeedMetersPerSecond,
    float TacticalArrivalToleranceMeters,
    int TacticalMoveActionPointCost,
    float FirstPersonMaximumSubstepMeters,
    float FirstPersonShotCooldownSeconds,
    float FirstPersonMeleeCooldownSeconds,
    float FirstPersonMeleeReachMeters,
    float FirstPersonMeleeHitRadiusMeters,
    float FirstPersonMinimumRangeMeters,
    float FirstPersonTargetHeightMeters,
    float FirstPersonMinimumForwardMeters,
    float FirstPersonHitRadiusMeters,
    float FirstPersonMetersPerWeaponRangeHex,
    int TacticalMinimumHitChancePercent,
    int TacticalMaximumHitChancePercent,
    int RangedPerceptionRangeMultiplier,
    int RangedPenaltyPerExcessHexPercent,
    int StrengthPenaltyPerPointPercent,
    int ReloadActionPointCost,
    int DeterministicDamageRollStride,
    int RatMovementLimitHexes,
    int RatAttackRangeHexes,
    int MinimumDamage);

internal sealed record Fo1CombatPresentationProfile(
    float TracerRadiusMeters,
    float TracerLifetimeSeconds,
    Color TracerColor,
    int MeshRadialSegments,
    int ImpactRings,
    float TracerEmissionEnergy,
    float ImpactEmissionEnergy,
    float MaterialRoughness,
    float ImpactRadiusMeters,
    float ImpactLifetimeSeconds,
    Color ImpactColor,
    float TacticalMissOffsetMeters,
    int RicochetEveryImpacts,
    float RicochetLengthMeters,
    Vector3 RicochetDirection,
    Color RicochetColor,
    float CasingLifetimeSeconds,
    float CasingMassKilograms,
    float CasingCollisionRadiusMeters,
    int CasingCollisionLayer,
    float CasingGroundHeightMeters,
    float CasingGroundHalfExtentMeters,
    float CasingGroundThicknessMeters,
    float CasingBounce,
    float CasingFriction,
    Vector3 CasingAngularVelocityRadiansPerSecond,
    float CasingEjectionSpeedMetersPerSecond,
    float CasingUpwardSpeedMetersPerSecond,
    float FpsCasingRightMeters,
    float FpsCasingDownMeters,
    float FpsCasingForwardMeters,
    float MeleeSweepRadiusMeters,
    float MeleeSweepLifetimeSeconds,
    Color MeleeSweepColor,
    float AudioUnitSizeMeters,
    float AudioMaximumDistanceMeters);

internal sealed record Fo1MobPresentationProfile(
    float SourceSpriteScale,
    float SelectedSourceSpriteScale,
    float SelectedCreatureScale,
    float RotationDegreesPerSourceStep,
    IReadOnlyList<string> IntactHiddenMeshNameFragments,
    int ExpectedIntactHiddenMeshes,
    Fo1SourceHighlightProfile SourceHighlight,
    Fo1CreatureHighlightProfile CreatureHighlight,
    Fo1HostileMarkerProfile HostileMarker,
    Fo1HostileBeaconProfile HostileBeacon,
    Fo1HealthLabelProfile HealthLabel,
    Fo1MobReadabilityProfile Readability,
    Fo1MobAnimationProfile Animation);

internal sealed record Fo1SourceHighlightProfile(
    Color NormalColor,
    float NormalMix,
    Color SelectedColor,
    float SelectedMix,
    Color DefeatedColor,
    float DefeatedMix,
    float DefeatedRollDegrees);

internal sealed record Fo1CreatureHighlightProfile(
    Color NormalColor,
    float NormalEnergy,
    Color SelectedColor,
    float SelectedEnergy,
    Color DefeatedColor,
    float DefeatedEnergy);

internal sealed record Fo1HostileMarkerProfile(
    float InnerRadiusMeters,
    float OuterRadiusMeters,
    float YOffsetMeters,
    Color NormalColor,
    Color NormalEmissionColor,
    Color SelectedColor,
    Color SelectedEmissionColor,
    float EmissionEnergy,
    float SelectedScale);

internal sealed record Fo1HostileBeaconProfile(
    float TopRadiusMeters,
    float BottomRadiusMeters,
    float HeightMeters,
    int RadialSegments,
    float YOffsetMeters,
    Color Color,
    Color EmissionColor,
    float EmissionEnergy,
    float SelectedScale);

internal sealed record Fo1HealthLabelProfile(
    float YOffsetMeters,
    int FontSize,
    float PixelSize,
    Color NormalColor,
    Color SelectedColor,
    Color DefeatedColor,
    int OutlineSize,
    float NormalScale,
    float SelectedScale);

internal sealed record Fo1MobReadabilityProfile(
    int TacticalRangeHexes,
    int PerspectiveRangeHexes,
    int BeaconRangeHexes);

internal sealed record Fo1MobAnimationProfile(
    float BlendSeconds,
    float MoveSeconds,
    float HitHoldSeconds,
    float AttackHoldSeconds,
    float DeathRollDegrees,
    float DeathRollSeconds);

internal sealed record Fo1CutawayProfile(
    int MinimumCandidateInstances,
    float MeltEdgeMeters,
    float TacticalEnvelopeCutHeightMeters,
    float PlayerFocusHeightMeters,
    float TargetFocusHeightMeters,
    float MinimumTargetDepthMarginMeters,
    float ScreenMarginPixels,
    float CameraClearanceMeters,
    IReadOnlyDictionary<string, float> MeltRadiusByRoleMeters,
    IReadOnlyDictionary<string, float> TacticalCutHeightByRoleMeters)
{
    internal float MeltRadius(string role) =>
        MeltRadiusByRoleMeters.GetValueOrDefault(role, MeltRadiusByRoleMeters["default"]);

    internal float TacticalCutHeight(string role) =>
        TacticalCutHeightByRoleMeters.GetValueOrDefault(role, TacticalCutHeightByRoleMeters["default"]);
}

internal sealed record Fo1ShowcaseProfile(
    int FixedFramesPerSecond,
    double AcceleratedOpeningScale,
    bool StageBannerVisible,
    int OpeningFadeOutFrames,
    int LandingFadeInFrames,
    int LandingHoldFrames,
    int VaultLookBackFrames,
    int VaultLookBackHoldFrames,
    int CaveLookFrames,
    int CaveLookHoldFrames,
    int FpsMoveMaximumHexes,
    int FpsMoveMaximumFrames,
    int FpsMoveHoldFrames,
    int FpsAimFrames,
    int FpsAimHoldFrames,
    int FpsMissAimFrames,
    int FpsMissHoldFrames,
    int FpsShotHoldFrames,
    int FpsKillHoldFrames,
    int FpsPostKillHoldFrames,
    int FpsMeleeApproachTurnFrames,
    int FpsMeleeAimFrames,
    int FpsMeleeAimHoldFrames,
    int FpsMeleeSwingHoldFrames,
    int FpsMeleeKillHoldFrames,
    int ReloadHoldFrames,
    int ShoulderOrbitFrames,
    int ShoulderMoveMaximumHexes,
    int ShoulderMoveMaximumFrames,
    int ShoulderMoveHoldFrames,
    int ModeTransitionHoldFrames,
    int GridHoldFrames,
    int TacticalTourFrames,
    int FinalHoldFrames,
    int MaximumFpsShots,
    int MaximumTacticalAttacks,
    float RatCorpseGroundToleranceMeters,
    float FpsAimTargetHeightMeters,
    double ShotCooldownWaitSeconds,
    double TacticalAttackSettleSeconds,
    int TacticalTargetHoldFrames,
    int TacticalFrameHoldFrames,
    int TacticalAttackHoldFrames,
    int TacticalKillHoldFrames,
    int FadeToTacticalOutFrames,
    int FadeToTacticalInFrames);
