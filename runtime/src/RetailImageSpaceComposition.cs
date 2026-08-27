using System.Globalization;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal sealed class RetailImageSpaceComposition
{
    private const int ColorCurveComponentCount = 5;

    private const string FormIdPrefix = "0x";
    private const int FormIdHexCharacters = 8;
    private const float MultiplyIdentity = 1.0f;
    private const float AddIdentity = 0.0f;
    private const float NoStrength = 0.0f;
    private static readonly Vector4 NeutralTint = new(1.0f, 1.0f, 1.0f, 0.0f);
    private static readonly Vector4 NeutralFade = Vector4.Zero;

    private readonly RetailImageSpaceConfiguration _configuration;
    private readonly IReadOnlyDictionary<uint, Modifier> _modifiers;

    private RetailImageSpaceComposition(
        RetailImageSpaceConfiguration configuration,
        IReadOnlyDictionary<uint, Modifier> modifiers)
    {
        _configuration = configuration;
        _modifiers = modifiers;
    }

    internal static RetailImageSpaceComposition Load(
        JsonElement source,
        RetailImageSpaceConfiguration configuration)
    {
        var modifiers = source.EnumerateArray()
            .Select(value => ParseModifier(value, configuration))
            .ToArray();
        if (modifiers.Select(value => value.FormId).Distinct().Count() != modifiers.Length)
            throw new InvalidOperationException(
                "Owned environment contains duplicate IMAD FormIDs.");
        return new RetailImageSpaceComposition(
            configuration,
            modifiers.ToDictionary(value => value.FormId));
    }

    internal ComposedImageSpace Compose(
        IReadOnlyList<float> baseTraits,
        IReadOnlyList<Contribution> contributions,
        ActorReviewContract.ImageSpaceShaderState capturedShader)
    {
        if (baseTraits.Count == 0 || baseTraits.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                "Owned base IMGS traits are incomplete.");
        var configuredIndices = _configuration.ModifierChannels
            .Select(channel => channel.TraitIndex)
            .Concat(_configuration.TraitIndices.Values());
        if (configuredIndices.Any(index => index >= baseTraits.Count))
            throw new InvalidOperationException(
                "Configured image-space trait index is outside the owned IMGS trait array.");

        var resolvedContributions = contributions.Select(contribution =>
        {
            if (!float.IsFinite(contribution.Strength) ||
                contribution.Strength < NoStrength ||
                !float.IsFinite(contribution.TimeSeconds) ||
                contribution.TimeSeconds < 0.0f)
                throw new InvalidOperationException(
                    $"Retail image-space contribution {contribution.SlotName} is invalid.");
            if (contribution.FormId == 0)
            {
                if (contribution.Strength != NoStrength)
                    throw new InvalidOperationException(
                        $"Retail image-space contribution {contribution.SlotName} has strength but no FormID.");
                return new ResolvedContribution(contribution, null);
            }
            if (!_modifiers.TryGetValue(contribution.FormId, out var modifier))
                throw new InvalidOperationException(
                    $"Retail image-space contribution references missing owned IMAD " +
                    $"0x{contribution.FormId:X8}.");
            return new ResolvedContribution(contribution, modifier);
        }).ToArray();

        var traits = baseTraits.ToArray();
        var multiplierDelta = new float[_configuration.ModifierChannels.Count];
        var additive = new float[_configuration.ModifierChannels.Count];
        var traitIndices = _configuration.TraitIndices;
        var baseTintStrength = MathF.Max(
            NoStrength,
            baseTraits[traitIndices.CinematicTintStrength]);
        var tintNumerator = new Vector3(
            baseTraits[traitIndices.CinematicTintRed],
            baseTraits[traitIndices.CinematicTintGreen],
            baseTraits[traitIndices.CinematicTintBlue]) * baseTintStrength;
        var tintWeight = baseTintStrength;
        var strongestTint = baseTintStrength;
        var tintStrengthByModifier = new Dictionary<uint, float>();
        var fadeNumerator = Vector3.Zero;
        var fadeWeight = NoStrength;
        var strongestFade = NoStrength;
        var fadeStrengthByModifier = new Dictionary<uint, float>();

        foreach (var resolved in resolvedContributions.Where(value => value.Modifier is not null))
        {
            var contribution = resolved.Source;
            var modifier = resolved.Modifier!;
            var strength = MathF.Max(NoStrength, contribution.Strength);
            for (var channelIndex = 0;
                 channelIndex < _configuration.ModifierChannels.Count;
                 ++channelIndex)
            {
                var channel = _configuration.ModifierChannels[channelIndex];
                multiplierDelta[channelIndex] +=
                    (Evaluate(
                        modifier.Multiply[channel.Name],
                        contribution.TimeSeconds,
                        MultiplyIdentity) - MultiplyIdentity) * strength;
                additive[channelIndex] += Evaluate(
                    modifier.Add[channel.Name],
                    contribution.TimeSeconds,
                    AddIdentity) * strength;
            }

            var tint = Evaluate(
                modifier.Tint,
                contribution.TimeSeconds,
                NeutralTint);
            var tintStrength = MathF.Max(NoStrength, tint.W) * strength;
            tintNumerator += new Vector3(tint.X, tint.Y, tint.Z) * tintStrength;
            tintWeight += tintStrength;
            tintStrengthByModifier[modifier.FormId] =
                tintStrengthByModifier.GetValueOrDefault(modifier.FormId) + tintStrength;

            var fade = Evaluate(
                modifier.Fade,
                contribution.TimeSeconds,
                NeutralFade);
            var fadeStrength = MathF.Max(NoStrength, fade.W) * strength;
            fadeNumerator += new Vector3(fade.X, fade.Y, fade.Z) * fadeStrength;
            fadeWeight += fadeStrength;
            fadeStrengthByModifier[modifier.FormId] =
                fadeStrengthByModifier.GetValueOrDefault(modifier.FormId) + fadeStrength;
        }

        foreach (var value in tintStrengthByModifier.Values)
            strongestTint = MathF.Max(strongestTint, value);
        foreach (var value in fadeStrengthByModifier.Values)
            strongestFade = MathF.Max(strongestFade, value);

        for (var channelIndex = 0;
             channelIndex < _configuration.ModifierChannels.Count;
             ++channelIndex)
        {
            var traitIndex = _configuration.ModifierChannels[channelIndex].TraitIndex;
            traits[traitIndex] = baseTraits[traitIndex] *
                (MultiplyIdentity + multiplierDelta[channelIndex]) + additive[channelIndex];
        }

        var tintResult = tintWeight > NoStrength
            ? new Vector4(
                tintNumerator.X / tintWeight,
                tintNumerator.Y / tintWeight,
                tintNumerator.Z / tintWeight,
                strongestTint)
            : NeutralTint;
        var fadeResult = fadeWeight > NoStrength
            ? new Vector4(
                fadeNumerator.X / fadeWeight,
                fadeNumerator.Y / fadeWeight,
                fadeNumerator.Z / fadeWeight,
                strongestFade)
            : NeutralFade;
        traits[traitIndices.CinematicTintRed] = tintResult.X;
        traits[traitIndices.CinematicTintGreen] = tintResult.Y;
        traits[traitIndices.CinematicTintBlue] = tintResult.Z;
        traits[traitIndices.CinematicTintStrength] = tintResult.W;

        var matchedAdaptation = capturedShader.Inputs.Single(
            input => input.Stage == _configuration.HdrBlend.BlurredAdaptationStage);
        if (matchedAdaptation.ConstantAlpha is not float matchedAdaptationSum ||
            !float.IsFinite(matchedAdaptationSum) || matchedAdaptationSum <= 0.0f)
            throw new InvalidOperationException(
                "Captured retail HDR adaptation state is unavailable.");

        var result = new ComposedImageSpace(
            traits,
            tintResult,
            fadeResult,
            matchedAdaptationSum,
            matchedAdaptation.Artifact.Sha256,
            resolvedContributions
                .Where(value => value.Modifier is not null)
                .Select(value => new AppliedModifier(
                    value.Source.SlotName,
                    value.Modifier!.FormId,
                    value.Modifier.EditorId,
                    value.Source.Strength,
                    value.Source.TimeSeconds,
                    value.Modifier.RecordSha256))
                .ToArray());
        ValidateCapturedShader(result, capturedShader);
        return result;
    }

    private void ValidateCapturedShader(
        ComposedImageSpace result,
        ActorReviewContract.ImageSpaceShaderState captured)
    {
        var indices = _configuration.TraitIndices;
        RequireRegister(
            new Vector4(result.Traits[indices.TargetLuminance], 0.0f, 0.0f, 0.0f),
            captured.HdrParameters,
            "HDR parameters");
        RequireRegister(
            new Vector4(
                result.Traits[indices.CinematicSaturation],
                result.Traits[indices.CinematicContrastAverageLuminance],
                result.Traits[indices.CinematicContrast],
                result.Traits[indices.CinematicBrightness]),
            captured.Cinematic,
            "cinematic");
        RequireRegister(result.Tint, captured.Tint, "tint");
        RequireRegister(result.Fade, captured.Fade, "fade");
    }

    private void RequireRegister(Vector4 actual, Vector4 captured, string label)
    {
        var error = new[]
        {
            MathF.Abs(actual.X - captured.X),
            MathF.Abs(actual.Y - captured.Y),
            MathF.Abs(actual.Z - captured.Z),
            MathF.Abs(actual.W - captured.W),
        }.Max();
        if (error > _configuration.ShaderConstantTolerance)
            throw new InvalidOperationException(
                $"Owned IMGS/IMAD {label} differs from the captured retail shader " +
                $"constant by {error:R}.");
    }

    private static Modifier ParseModifier(
        JsonElement source,
        RetailImageSpaceConfiguration configuration)
    {
        var formId = ParseFormId(source.GetProperty("formId").GetString(), "IMAD");
        var duration = source.GetProperty("duration").GetSingle();
        if (!float.IsFinite(duration) || duration < 0.0f)
            throw new InvalidOperationException($"Owned IMAD 0x{formId:X8} duration is invalid.");
        return new Modifier(
            formId,
            RequireText(source, "editorId"),
            source.GetProperty("adapterFlags").GetUInt32(),
            duration,
            ParseFloatChannels(source.GetProperty("multiply"), configuration, "multiply"),
            ParseFloatChannels(source.GetProperty("add"), configuration, "add"),
            ParseColorCurve(source.GetProperty("tint"), $"IMAD 0x{formId:X8} tint"),
            ParseColorCurve(source.GetProperty("fade"), $"IMAD 0x{formId:X8} fade"),
            RequireText(source, "recordSha256"));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<FloatKey>> ParseFloatChannels(
        JsonElement source,
        RetailImageSpaceConfiguration configuration,
        string role)
    {
        var properties = source.EnumerateObject().ToArray();
        var expected = configuration.ModifierChannels
            .Select(channel => channel.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (properties.Length != expected.Count ||
            !properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(expected))
            throw new InvalidOperationException(
                $"Owned IMAD {role} channels differ from the configured Fallout contract.");
        return properties.ToDictionary(
            property => property.Name,
            property => (IReadOnlyList<FloatKey>)ParseFloatCurve(
                property.Value,
                $"IMAD {role} {property.Name}"),
            StringComparer.Ordinal);
    }

    private static FloatKey[] ParseFloatCurve(JsonElement source, string label)
    {
        var keys = source.EnumerateArray().Select(value =>
        {
            var components = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
            if (components.Length != 2 || components.Any(item => !float.IsFinite(item)))
                throw new InvalidOperationException($"Owned {label} key is invalid.");
            return new FloatKey(components[0], components[1]);
        }).ToArray();
        RequireOrdered(keys.Select(key => key.TimeSeconds), label);
        return keys;
    }

    private static ColorKey[] ParseColorCurve(JsonElement source, string label)
    {
        var keys = source.EnumerateArray().Select(value =>
        {
            var components = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
            if (components.Length != ColorCurveComponentCount ||
                components.Any(item => !float.IsFinite(item)))
                throw new InvalidOperationException($"Owned {label} key is invalid.");
            return new ColorKey(
                components[0],
                new Vector4(components[1], components[2], components[3], components[4]));
        }).ToArray();
        RequireOrdered(keys.Select(key => key.TimeSeconds), label);
        return keys;
    }

    private static void RequireOrdered(IEnumerable<float> times, string label)
    {
        var values = times.ToArray();
        if (values.Any(value => value < 0.0f) ||
            values.Zip(values.Skip(1), (left, right) => right < left).Any(value => value))
            throw new InvalidOperationException($"Owned {label} keys are not time-ordered.");
    }

    private static float Evaluate(IReadOnlyList<FloatKey> keys, float time, float neutral)
    {
        if (keys.Count == 0)
            return neutral;
        if (keys.Count == 1 || time <= keys[0].TimeSeconds)
            return keys[0].Value;
        for (var index = 1; index < keys.Count; ++index)
        {
            if (time > keys[index].TimeSeconds)
                continue;
            var duration = keys[index].TimeSeconds - keys[index - 1].TimeSeconds;
            if (duration <= 0.0f)
                return keys[index].Value;
            var factor = Mathf.Clamp(
                (time - keys[index - 1].TimeSeconds) / duration,
                0.0f,
                1.0f);
            return Mathf.Lerp(keys[index - 1].Value, keys[index].Value, factor);
        }
        return keys[^1].Value;
    }

    private static Vector4 Evaluate(
        IReadOnlyList<ColorKey> keys,
        float time,
        Vector4 neutral)
    {
        if (keys.Count == 0)
            return neutral;
        if (keys.Count == 1 || time <= keys[0].TimeSeconds)
            return keys[0].Value;
        for (var index = 1; index < keys.Count; ++index)
        {
            if (time > keys[index].TimeSeconds)
                continue;
            var duration = keys[index].TimeSeconds - keys[index - 1].TimeSeconds;
            if (duration <= 0.0f)
                return keys[index].Value;
            var factor = Mathf.Clamp(
                (time - keys[index - 1].TimeSeconds) / duration,
                0.0f,
                1.0f);
            return keys[index - 1].Value.Lerp(keys[index].Value, factor);
        }
        return keys[^1].Value;
    }

    private static uint ParseFormId(string? value, string label)
    {
        if (value is null || value.Length != FormIdPrefix.Length + FormIdHexCharacters ||
            !value.StartsWith(FormIdPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Owned {label} FormID is not canonical.");
        return uint.Parse(
            value.AsSpan(FormIdPrefix.Length),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture);
    }

    private static string RequireText(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Owned IMAD {property} is empty.");
        return value;
    }

    internal readonly record struct Contribution(
        string SlotName,
        uint FormId,
        float Strength,
        float TimeSeconds);

    internal readonly record struct AppliedModifier(
        string SlotName,
        uint FormId,
        string EditorId,
        float Strength,
        float TimeSeconds,
        string RecordSha256);

    internal readonly record struct ComposedImageSpace(
        IReadOnlyList<float> Traits,
        Vector4 Tint,
        Vector4 Fade,
        float MatchedAdaptationSum,
        string MatchedAdaptationSourceSha256,
        IReadOnlyList<AppliedModifier> AppliedModifiers);

    private sealed record Modifier(
        uint FormId,
        string EditorId,
        uint AdapterFlags,
        float DurationSeconds,
        IReadOnlyDictionary<string, IReadOnlyList<FloatKey>> Multiply,
        IReadOnlyDictionary<string, IReadOnlyList<FloatKey>> Add,
        IReadOnlyList<ColorKey> Tint,
        IReadOnlyList<ColorKey> Fade,
        string RecordSha256);

    private readonly record struct ResolvedContribution(
        Contribution Source,
        Modifier? Modifier);

    private readonly record struct FloatKey(float TimeSeconds, float Value);
    private readonly record struct ColorKey(float TimeSeconds, Vector4 Value);
}
