using System.Globalization;
using System.Text.Json;
using Godot;


using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics.Capture;

namespace OpenNV.Runtime.Presentation.Rendering;

internal sealed class RetailExteriorEnvironment
{
    private const float SymmetricIntervalHalf = 0.5f;

    private const string EnvironmentSchema = "opennv-fallout-environment/v2";
    private const string FormIdPrefix = "0x";
    private const int FormIdHexCharacters = 8;
    private const int WeatherSampleCount = 6;
    private const int WeatherCloudLayerCount = 4;
    private const int WeatherFogValueCount = 6;
    private const int ImageSpaceTraitCount = 33;
    private const int SunriseSample = 0;
    private const int DaySample = 1;
    private const int SunsetSample = 2;
    private const int NightSample = 3;
    private const int HighNoonSample = 4;
    private const float HighNoonHour = 12.0f;
    private const float HoursPerDay = 24.0f;
    private const float DaytimeColorExtensionHours = 0.5f;
    private const float CompleteWeatherPercentage = 1.0f;
    private const float CapturedColorTolerance = 0.000001f;
    private const int DayFogNearIndex = 0;
    private const int DayFogFarIndex = 1;
    private const int DayFogPowerIndex = 4;

    private static readonly string[] RequiredColorRows =
    [
        "skyUpper",
        "fog",
        "ambient",
        "sunlight",
        "sun",
        "stars",
        "skyLower",
        "horizon",
    ];

    private static readonly string[] RequiredCloudSurfaceSemantics =
    [
        "weather-cloud-layer-geometry",
        "horizon-clear",
        "horizon-overcast",
        "lower-layer",
    ];

    private static readonly string[] WeatherTimeNames =
    [
        "sunrise",
        "day",
        "sunset",
        "night",
        "highNoon",
        "midnight",
    ];

    private readonly IReadOnlyDictionary<uint, WeatherRecord> _weather;
    private readonly RetailImageSpaceComposition _imageSpaceComposition;
    private readonly float _imageSpaceConstantTolerance;

    private RetailExteriorEnvironment(
        uint worldspaceFormId,
        ClimateState climate,
        IReadOnlyDictionary<uint, WeatherRecord> weather,
        ImageSpaceState imageSpace,
        RetailImageSpaceComposition imageSpaceComposition,
        float imageSpaceConstantTolerance,
        IReadOnlyDictionary<string, TextureEvidence> textures,
        IReadOnlyDictionary<string, SkyModelEvidence> skyModels,
        IReadOnlySet<string> missingTextures)
    {
        WorldspaceFormId = worldspaceFormId;
        Climate = climate;
        _weather = weather;
        ImageSpace = imageSpace;
        _imageSpaceComposition = imageSpaceComposition;
        _imageSpaceConstantTolerance = imageSpaceConstantTolerance;
        Textures = textures;
        SkyModels = skyModels;
        MissingTextures = missingTextures;
    }

    internal uint WorldspaceFormId { get; }
    internal ClimateState Climate { get; }
    internal ImageSpaceState ImageSpace { get; }
    internal IReadOnlyDictionary<string, TextureEvidence> Textures { get; }
    internal IReadOnlyDictionary<string, SkyModelEvidence> SkyModels { get; }
    internal IReadOnlySet<string> MissingTextures { get; }

    internal static RetailExteriorEnvironment Load(
        JsonElement scene,
        RetailImageSpaceConfiguration imageSpaceConfiguration)
    {
        var source = scene.GetProperty("environmentCatalog");
        if (source.GetProperty("schema").GetString() != EnvironmentSchema)
            throw new InvalidOperationException("Owned CELL has an unexpected environment schema.");
        var cellWorldspace = ParseCellFormId(
            scene.GetProperty("cell").GetProperty("worldspaceFormId").GetString(),
            "CELL worldspace");
        var worldspace = source.GetProperty("worldspace");
        var worldspaceForm = ParseFormId(
            worldspace.GetProperty("formId").GetString(),
            "environment worldspace");
        if (cellWorldspace != worldspaceForm)
            throw new InvalidOperationException(
                "Owned CELL environment belongs to another worldspace.");

        var climate = ParseClimate(source.GetProperty("climate"));
        var linkedClimate = ParseFormId(
            worldspace.GetProperty("climateFormId").GetString(),
            "worldspace climate");
        if (linkedClimate != climate.FormId)
            throw new InvalidOperationException(
                "Owned worldspace and climate records do not form one exact relationship.");

        var weather = source.GetProperty("weather").EnumerateArray()
            .Select(ParseWeather)
            .ToDictionary(value => value.FormId);
        if (weather.Count == 0)
            throw new InvalidOperationException("Owned environment catalog contains no WTHR records.");
        var imageSpace = ParseImageSpace(source.GetProperty("baseImageSpace"));
        var linkedImageSpace = ParseFormId(
            worldspace.GetProperty("imageSpaceFormId").GetString(),
            "worldspace image space");
        if (linkedImageSpace != imageSpace.FormId)
            throw new InvalidOperationException(
                "Owned worldspace and base image-space records do not match.");
        var imageSpaceComposition = RetailImageSpaceComposition.Load(
            source.GetProperty("imageSpaceModifiers"),
            imageSpaceConfiguration);

        var textures = source.GetProperty("textures").EnumerateArray()
            .Select(ParseTexture)
            .ToDictionary(value => value.AuthoredPath, StringComparer.Ordinal);
        var missing = source.GetProperty("missingTextures").EnumerateArray()
            .Select(value => CanonicalPath(value.GetString()))
            .ToHashSet(StringComparer.Ordinal);
        if (textures.Keys.Intersect(missing, StringComparer.Ordinal).Any())
            throw new InvalidOperationException(
                "Environment texture cannot be both decoded and authored-missing.");
        var sceneTextureIds = scene.GetProperty("textures").EnumerateArray()
            .Select(value => RequireText(value, "id"))
            .ToHashSet(StringComparer.Ordinal);
        var skyModels = source.GetProperty("skyModels").EnumerateObject()
            .Select(value => ParseSkyModel(value, sceneTextureIds))
            .ToDictionary(value => value.Role, StringComparer.Ordinal);
        if (!skyModels.ContainsKey("atmosphere") || !skyModels.ContainsKey("clouds") ||
            !skyModels.ContainsKey("nightSky"))
            throw new InvalidOperationException(
                "Owned environment has no exact atmosphere/cloud/night-sky model set.");
        return new RetailExteriorEnvironment(
            worldspaceForm,
            climate,
            weather,
            imageSpace,
            imageSpaceComposition,
            imageSpaceConfiguration.ShaderConstantTolerance,
            textures,
            skyModels,
            missing);
    }

    internal ResolvedEnvironment Resolve(ActorReviewContract.EnvironmentState captured)
    {
        if (captured.WeatherPercent != CompleteWeatherPercentage)
            throw new InvalidOperationException(
                "Actor review environment is not a completed retail weather transition.");
        var effectiveWeatherForm = captured.WeatherForm == 0u
            ? captured.DefaultWeatherForm
            : captured.WeatherForm;
        if (!_weather.TryGetValue(effectiveWeatherForm, out var current))
            throw new InvalidOperationException(
                $"Captured effective WTHR is absent from the owned master: " +
                $"0x{effectiveWeatherForm:X8}");
        if (!_weather.ContainsKey(captured.DefaultWeatherForm))
            throw new InvalidOperationException(
                $"Captured default WTHR is absent from the owned master: " +
                $"0x{captured.DefaultWeatherForm:X8}");
        if (current.SampleCount != WeatherSampleCount)
            throw new InvalidOperationException(
                "Captured retail weather lacks its exact six-sample color tables.");
        if (captured.ImageSpace.FormId != ImageSpace.FormId ||
            !captured.ImageSpace.Traits.SequenceEqual(ImageSpace.Traits))
            throw new InvalidOperationException(
                "Captured base image space differs from the owned IMGS record.");

        var blend = Blend(captured.GameHour, Climate);
        var contributions = ResolveImageSpaceContributions(current, blend, captured);
        var composedImageSpace = _imageSpaceComposition.Compose(
            ImageSpace.Traits,
            contributions,
            captured.ImageSpaceShader);
        var colors = current.Colors.ToDictionary(
            value => value.Key,
            value => Interpolate(value.Value, blend),
            StringComparer.Ordinal);
        RequireColor(colors["ambient"], captured.AmbientColor, "ambient");
        RequireColor(colors["sunlight"], captured.DirectionalColor, "sunlight");
        RequireColor(colors["sunlight"], captured.FogColor, "sun-fog");

        var cloudColors = current.CloudColors
            .Select(samples => Interpolate(samples, blend))
            .ToArray();
        var cloudTextures = current.CloudTextures.Select(path =>
        {
            if (path.Length == 0)
                return (TextureEvidence?)null;
            if (MissingTextures.Contains(path) || !Textures.TryGetValue(path, out var texture))
                throw new InvalidOperationException(
                    $"Captured WTHR cloud texture is unavailable: {path}");
            return texture;
        }).ToArray();
        return new ResolvedEnvironment(
            current.FormId,
            current.EditorId,
            captured.GameHour,
            captured.SkyMode,
            blend,
            colors["skyUpper"],
            colors["skyLower"],
            colors["horizon"],
            colors["fog"],
            colors["ambient"],
            colors["sunlight"],
            colors["sun"],
            colors["stars"],
            cloudColors,
            cloudTextures,
            current.CloudSpeeds,
            current.FogDistances[DayFogNearIndex],
            current.FogDistances[DayFogFarIndex],
            current.FogDistances[DayFogPowerIndex],
            composedImageSpace);
    }

    internal ResolvedEnvironment ResolveConfiguredClearDay() =>
        ResolveClimateWeather(ResolveUnconditionalClimateWeather(), Climate.SunriseEndHour);

    internal uint ResolveUnconditionalClimateWeather()
    {
        var defaults = Climate.WeatherEntries
            .Where(entry => entry.GlobalFormId is null && entry.Chance == 100)
            .ToArray();
        if (defaults.Length != 1)
            throw new InvalidOperationException(
                "Configured clear-day mode requires one unconditional 100-percent CLMT weather.");
        return defaults[0].WeatherFormId;
    }

    internal ResolvedEnvironment ResolveClimateWeather(uint weatherFormId, float gameHour)
    {
        if (!Climate.WeatherEntries.Any(entry => entry.WeatherFormId == weatherFormId))
            throw new InvalidOperationException(
                $"Selected WTHR is not owned by CLMT 0x{Climate.FormId:X8}: " +
                $"0x{weatherFormId:X8}");
        if (!_weather.TryGetValue(weatherFormId, out var current))
            throw new InvalidOperationException(
                $"Default CLMT weather is absent from the owned master: " +
                $"0x{weatherFormId:X8}");
        if (current.SampleCount != WeatherSampleCount)
            throw new InvalidOperationException(
                "Default CLMT weather lacks its exact six-sample color tables.");

        var blend = Blend(gameHour, Climate);
        var colors = current.Colors.ToDictionary(
            value => value.Key,
            value => Interpolate(value.Value, blend),
            StringComparer.Ordinal);
        var cloudColors = current.CloudColors
            .Select(samples => Interpolate(samples, blend))
            .ToArray();
        var cloudTextures = current.CloudTextures.Select(path =>
        {
            if (path.Length == 0)
                return (TextureEvidence?)null;
            if (MissingTextures.Contains(path) || !Textures.TryGetValue(path, out var texture))
                throw new InvalidOperationException(
                    $"Default WTHR cloud texture is unavailable: {path}");
            return texture;
        }).ToArray();
        var baseImageSpace = new RetailImageSpaceComposition.ComposedImageSpace(
            ImageSpace.Traits,
            new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
            Vector4.Zero,
            1.0f,
            ImageSpace.SourceSha256,
            Array.Empty<RetailImageSpaceComposition.AppliedModifier>());
        return new ResolvedEnvironment(
            current.FormId,
            current.EditorId,
            gameHour,
            0u,
            blend,
            colors["skyUpper"],
            colors["skyLower"],
            colors["horizon"],
            colors["fog"],
            colors["ambient"],
            colors["sunlight"],
            colors["sun"],
            colors["stars"],
            cloudColors,
            cloudTextures,
            current.CloudSpeeds,
            current.FogDistances[DayFogNearIndex],
            current.FogDistances[DayFogFarIndex],
            current.FogDistances[DayFogPowerIndex],
            baseImageSpace);
    }

    private IReadOnlyList<RetailImageSpaceComposition.Contribution>
        ResolveImageSpaceContributions(
            WeatherRecord weather,
            TimeBlend blend,
            ActorReviewContract.EnvironmentState captured)
    {
        var slots = captured.WeatherImageSpace.ToDictionary(
            slot => slot.Name,
            StringComparer.Ordinal);
        if (slots.Count != captured.WeatherImageSpace.Count)
            throw new InvalidOperationException(
                "Captured retail weather image-space slots are not unique.");
        var currentFadeIn = slots["currentFadeIn"];
        var currentFadeOut = slots["currentFadeOut"];
        var transitionFadeIn = slots["transitionFadeIn"];
        var transitionFadeOut = slots["transitionFadeOut"];
        RequireImageSpaceSlot(
            currentFadeIn,
            weather.ImageSpaceModifierForms[blend.Primary] ?? 0u,
            blend.PrimaryStrength);
        RequireImageSpaceSlot(
            currentFadeOut,
            weather.ImageSpaceModifierForms[blend.Secondary] ?? 0u,
            1.0f - blend.PrimaryStrength);
        RequireImageSpaceSlot(transitionFadeIn, 0u, 0.0f);
        RequireImageSpaceSlot(transitionFadeOut, 0u, 0.0f);
        return captured.WeatherImageSpace.Select(slot =>
            new RetailImageSpaceComposition.Contribution(
                slot.Name,
                slot.FormId,
                slot.Strength,
                slot.AgeSeconds)).ToArray();
    }

    private void RequireImageSpaceSlot(
        ActorReviewContract.WeatherImageSpaceSlot actual,
        uint expectedForm,
        float expectedStrength)
    {
        if (actual.FormId != expectedForm || actual.PreviousFormId != 0u ||
            MathF.Abs(actual.Strength - expectedStrength) > _imageSpaceConstantTolerance)
            throw new InvalidOperationException(
                $"Captured retail weather image-space slot {actual.Name} differs from " +
                "the owned WTHR/time blend.");
    }

    private static ClimateState ParseClimate(JsonElement source)
    {
        var timing = source.GetProperty("timing");
        var weatherEntries = source.GetProperty("weatherEntries").EnumerateArray()
            .Select(entry => new ClimateWeatherEntry(
                ParseFormId(entry.GetProperty("weatherFormId").GetString(), "climate weather"),
                entry.GetProperty("chance").GetByte(),
                entry.GetProperty("globalFormId").ValueKind == JsonValueKind.Null
                    ? null
                    : ParseFormId(
                        entry.GetProperty("globalFormId").GetString(),
                        "climate weather global")))
            .ToArray();
        if (weatherEntries.Length == 0)
            throw new InvalidOperationException("Owned climate contains no weather entries.");
        return new ClimateState(
            ParseFormId(source.GetProperty("formId").GetString(), "climate"),
            RequireText(source, "editorId"),
            timing.GetProperty("sunriseBeginHour").GetSingle(),
            timing.GetProperty("sunriseEndHour").GetSingle(),
            timing.GetProperty("sunsetBeginHour").GetSingle(),
            timing.GetProperty("sunsetEndHour").GetSingle(),
            weatherEntries);
    }

    private static ImageSpaceState ParseImageSpace(JsonElement source)
    {
        var traits = source.GetProperty("effectiveTraitArray").EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (traits.Length != ImageSpaceTraitCount || traits.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                "Owned IMGS record has no complete finite trait array.");
        return new ImageSpaceState(
            ParseFormId(source.GetProperty("formId").GetString(), "image space"),
            RequireText(source, "editorId"),
            traits,
            RequireText(source, "dnamSha256"));
    }

    private static WeatherRecord ParseWeather(JsonElement source)
    {
        var sampleCount = source.GetProperty("sampleCount").GetInt32();
        var colors = new Dictionary<string, IReadOnlyList<Color>>(StringComparer.Ordinal);
        var colorSource = source.GetProperty("colors");
        foreach (var name in RequiredColorRows)
            colors[name] = ReadColors(colorSource.GetProperty(name), sampleCount, name);
        var cloudColors = source.GetProperty("cloudColors").EnumerateArray()
            .Select((value, index) => ReadColors(value, sampleCount, $"cloud {index}"))
            .ToArray();
        if (cloudColors.Length != WeatherCloudLayerCount)
            throw new InvalidOperationException("WTHR has an invalid cloud-color layer count.");
        var cloudTextures = source.GetProperty("cloudTextures").EnumerateArray()
            .Select(value => CanonicalPath(value.GetString()))
            .ToArray();
        var cloudSpeeds = source.GetProperty("cloudSpeeds").EnumerateArray()
            .Select(value => value.GetByte())
            .ToArray();
        if (cloudTextures.Length != WeatherCloudLayerCount ||
            cloudSpeeds.Length != WeatherCloudLayerCount)
            throw new InvalidOperationException("WTHR has an invalid cloud-layer contract.");
        var fog = source.GetProperty("fogDistances").EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (fog.Length != WeatherFogValueCount || fog.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("WTHR has invalid fog distances.");
        var modifierSource = source.GetProperty("imageSpaceModifiers");
        var modifierProperties = modifierSource.EnumerateObject().ToArray();
        if (modifierProperties.Length != WeatherTimeNames.Length ||
            !modifierProperties.Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal).SetEquals(WeatherTimeNames))
            throw new InvalidOperationException(
                "WTHR has an invalid image-space modifier contract.");
        var modifierForms = WeatherTimeNames.Select(name =>
        {
            var value = modifierSource.GetProperty(name);
            return value.ValueKind == JsonValueKind.Null
                ? (uint?)null
                : ParseFormId(value.GetString(), $"weather {name} image-space modifier");
        }).ToArray();
        return new WeatherRecord(
            ParseFormId(source.GetProperty("formId").GetString(), "weather"),
            RequireText(source, "editorId"),
            sampleCount,
            colors,
            cloudColors,
            cloudTextures,
            cloudSpeeds,
            fog,
            modifierForms);
    }

    private static TextureEvidence ParseTexture(JsonElement source)
    {
        var path = VerifiedGltfLoader.ResolvePath(RequireText(source, "png"));
        var hash = RequireText(source, "pngSha256").ToLowerInvariant();
        VerifiedGltfLoader.VerifyHash(path, hash);
        return new TextureEvidence(
            CanonicalPath(source.GetProperty("authoredPath").GetString()),
            RequireText(source, "artifactId"),
            path,
            hash);
    }

    private static SkyModelEvidence ParseSkyModel(
        JsonProperty property,
        IReadOnlySet<string> sceneTextureIds)
    {
        var source = property.Value;
        var surfaces = source.GetProperty("surfaces").EnumerateArray()
            .Select(value => new SkySurfaceEvidence(
                value.GetProperty("index").GetInt32(),
                RequireText(value, "name"),
                value.GetProperty("attributes").EnumerateArray()
                    .Select(attribute => attribute.GetString() ?? "")
                    .ToArray(),
                RequireText(value, "semantic"),
                value.TryGetProperty("diffuseTextureId", out var textureId) &&
                    textureId.ValueKind == JsonValueKind.String
                    ? textureId.GetString()
                    : null))
            .ToArray();
        if (surfaces.Length == 0 ||
            !surfaces.Select(value => value.Index).SequenceEqual(Enumerable.Range(0, surfaces.Length)) ||
            surfaces.Any(value => !value.Attributes.Contains("COLOR_0", StringComparer.Ordinal) ||
                !value.Attributes.Contains("TEXCOORD_0", StringComparer.Ordinal)))
            throw new InvalidOperationException(
                $"Owned {property.Name} sky model lacks its ordered color/UV surfaces.");
        if (property.Name == "clouds" &&
            !surfaces.Select(value => value.Semantic)
                .SequenceEqual(RequiredCloudSurfaceSemantics, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Owned cloud model lacks its exact engine semantic surface routes.");
        if (property.Name == "atmosphere" &&
            (surfaces.Length != 1 || surfaces[0].Semantic != "atmosphere"))
            throw new InvalidOperationException(
                "Owned atmosphere model lacks its exact semantic surface route.");
        if (property.Name == "nightSky" && surfaces.Any(surface =>
                string.IsNullOrEmpty(surface.DiffuseTextureId) ||
                !sceneTextureIds.Contains(surface.DiffuseTextureId)))
            throw new InvalidOperationException(
                "Owned night-sky surface texture is absent from the CELL texture inventory.");
        return new SkyModelEvidence(
            property.Name,
            CanonicalPath(source.GetProperty("authoredPath").GetString()),
            RequireText(source, "assetId"),
            VerifiedGltfLoader.ResolvePath(RequireText(source, "model")),
            VerifiedGltfLoader.ResolvePath(RequireText(source, "sidecar")),
            surfaces);
    }

    private static IReadOnlyList<Color> ReadColors(
        JsonElement source,
        int expected,
        string label)
    {
        var result = source.EnumerateArray().Select(value =>
        {
            var channels = value.EnumerateArray().Select(channel => channel.GetByte()).ToArray();
            if (channels.Length != 4)
                throw new InvalidOperationException($"WTHR {label} sample is not RGBA.");
            return new Color(
                channels[0] / (float)byte.MaxValue,
                channels[1] / (float)byte.MaxValue,
                channels[2] / (float)byte.MaxValue,
                channels[3] / (float)byte.MaxValue);
        }).ToArray();
        if (result.Length != expected)
            throw new InvalidOperationException(
                $"WTHR {label} has {result.Length} samples, expected {expected}.");
        return result;
    }

    private static TimeBlend Blend(float gameHour, ClimateState timing)
    {
        if (!float.IsFinite(gameHour) || gameHour < 0.0f || gameHour >= HoursPerDay)
            throw new InvalidOperationException("Captured FNV game hour is outside [0, 24).");
        var nightEnd = timing.SunriseBeginHour - DaytimeColorExtensionHours;
        var dayStart = timing.SunriseEndHour;
        var dayEnd = timing.SunsetBeginHour;
        var nightStart = timing.SunsetEndHour + DaytimeColorExtensionHours;
        if (gameHour <= nightEnd || gameHour >= nightStart)
            return new TimeBlend(NightSample, NightSample, 1.0f);
        if (gameHour > nightEnd && gameHour < dayStart)
        {
            var midpoint = (nightEnd + dayStart) * SymmetricIntervalHalf;
            var halfDuration = (dayStart - nightEnd) * SymmetricIntervalHalf;
            return gameHour < midpoint
                ? new TimeBlend(
                    SunriseSample,
                    NightSample,
                    Mathf.Clamp((gameHour - nightEnd) / halfDuration, 0.0f, 1.0f))
                : new TimeBlend(
                    SunriseSample,
                    DaySample,
                    Mathf.Clamp((dayStart - gameHour) / halfDuration, 0.0f, 1.0f));
        }
        if (gameHour > dayStart && gameHour < HighNoonHour)
            return new TimeBlend(
                HighNoonSample,
                DaySample,
                Mathf.Clamp(
                    (gameHour - dayStart) / (HighNoonHour - dayStart),
                    0.0f,
                    1.0f));
        if (gameHour > HighNoonHour && gameHour < dayEnd)
            return new TimeBlend(
                DaySample,
                HighNoonSample,
                Mathf.Clamp(
                    (gameHour - HighNoonHour) / (dayEnd - HighNoonHour),
                    0.0f,
                    1.0f));
        if (gameHour > dayEnd && gameHour < nightStart)
        {
            var midpoint = (dayEnd + nightStart) * SymmetricIntervalHalf;
            var halfDuration = (nightStart - dayEnd) * SymmetricIntervalHalf;
            return gameHour < midpoint
                ? new TimeBlend(
                    SunsetSample,
                    DaySample,
                    Mathf.Clamp((gameHour - dayEnd) / halfDuration, 0.0f, 1.0f))
                : new TimeBlend(
                    SunsetSample,
                    NightSample,
                    Mathf.Clamp((nightStart - gameHour) / halfDuration, 0.0f, 1.0f));
        }
        return new TimeBlend(DaySample, DaySample, 1.0f);
    }

    private static Color Interpolate(IReadOnlyList<Color> samples, TimeBlend blend) =>
        samples[blend.Secondary].Lerp(samples[blend.Primary], blend.PrimaryStrength);

    private static void RequireColor(Color actual, Color captured, string label)
    {
        var error = new Vector3(actual.R, actual.G, actual.B)
            .DistanceTo(new Vector3(captured.R, captured.G, captured.B));
        if (error > CapturedColorTolerance)
            throw new InvalidOperationException(
                $"Owned WTHR {label} differs from retail capture by {error:R}.");
    }

    private static uint ParseFormId(string? value, string label)
    {
        if (value is null || value.Length != FormIdPrefix.Length + FormIdHexCharacters ||
            !value.StartsWith(FormIdPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label} is not a canonical FormID.");
        return uint.Parse(
            value.AsSpan(FormIdPrefix.Length),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture);
    }

    private static uint ParseCellFormId(string? value, string label)
    {
        if (value is null || value.Length != FormIdHexCharacters ||
            value.Any(character => character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f')))
            throw new InvalidOperationException($"{label} is not a canonical CELL FormID.");
        return uint.Parse(
            value.AsSpan(),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture);
    }

    private static string CanonicalPath(string? path) =>
        (path ?? "").Trim().Replace('/', '\\').TrimStart('\\').ToLowerInvariant();

    private static string RequireText(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Environment {property} is empty.");
        return value;
    }

    internal readonly record struct ClimateState(
        uint FormId,
        string EditorId,
        float SunriseBeginHour,
        float SunriseEndHour,
        float SunsetBeginHour,
        float SunsetEndHour,
        IReadOnlyList<ClimateWeatherEntry> WeatherEntries);

    internal readonly record struct ClimateWeatherEntry(
        uint WeatherFormId,
        byte Chance,
        uint? GlobalFormId);

    internal readonly record struct ImageSpaceState(
        uint FormId,
        string EditorId,
        IReadOnlyList<float> Traits,
        string SourceSha256);

    internal readonly record struct TimeBlend(
        int Primary,
        int Secondary,
        float PrimaryStrength);

    internal readonly record struct TextureEvidence(
        string AuthoredPath,
        string AssetId,
        string PngPath,
        string PngSha256);

    internal readonly record struct SkyModelEvidence(
        string Role,
        string AuthoredPath,
        string AssetId,
        string ModelPath,
        string SidecarPath,
        IReadOnlyList<SkySurfaceEvidence> Surfaces);

    internal readonly record struct SkySurfaceEvidence(
        int Index,
        string Name,
        IReadOnlyList<string> Attributes,
        string Semantic,
        string? DiffuseTextureId);

    internal readonly record struct ResolvedEnvironment(
        uint WeatherFormId,
        string WeatherEditorId,
        float GameHour,
        uint SkyMode,
        TimeBlend Blend,
        Color SkyUpperEncoded,
        Color SkyLowerEncoded,
        Color HorizonEncoded,
        Color FogEncoded,
        Color AmbientEncoded,
        Color SunlightEncoded,
        Color SunEncoded,
        Color StarsEncoded,
        IReadOnlyList<Color> CloudColorsEncoded,
        IReadOnlyList<TextureEvidence?> CloudTextures,
        IReadOnlyList<byte> CloudSpeeds,
        float FogNearGameUnits,
        float FogFarGameUnits,
        float FogPower,
        RetailImageSpaceComposition.ComposedImageSpace ImageSpace);

    private readonly record struct WeatherRecord(
        uint FormId,
        string EditorId,
        int SampleCount,
        IReadOnlyDictionary<string, IReadOnlyList<Color>> Colors,
        IReadOnlyList<IReadOnlyList<Color>> CloudColors,
        IReadOnlyList<string> CloudTextures,
        IReadOnlyList<byte> CloudSpeeds,
        IReadOnlyList<float> FogDistances,
        IReadOnlyList<uint?> ImageSpaceModifierForms);
}
