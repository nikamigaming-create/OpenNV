using System.Numerics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActiveImageModifier(FalloutImageSpaceModifier Source, double ElapsedSeconds);
internal sealed record FalloutWeatherImageModifier(int Slot, FalloutImageSpaceModifier? Source, float Strength)
{
    internal bool EngineDefault => Source is null;
}

internal sealed record FalloutImageSpaceFrame(float TargetLuminance, Vector4 Cinematic, Vector4 Tint, Vector4 Fade,
    IReadOnlyList<FalloutActiveImageModifier> Active, IReadOnlyList<string> UnboundChannels, float BlurRadius = 0,
    Vector2 DoubleVisionOffset = default, IReadOnlyList<FalloutWeatherImageModifier>? Weather = null, float SunlightDimmer = 1, float SkyScale = 1);

/// <summary>Gameplay-clock IMAD lifetime and source IMGS/IMAD composition, independent of captured state.</summary>
internal sealed class FalloutImageSpaceState
{
    // IMAD channel order differs from IMGS storage: Skin Dimmer is channel 2;
    // cinematic order is saturation, average luminance, contrast, brightness.
    private static readonly int[] TraitIndices = [0, 1, 14, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 25, 26, 27, 28];
    private readonly Dictionary<FalloutFormKey, FalloutActiveImageModifier> _active = [];
    internal IReadOnlyCollection<FalloutActiveImageModifier> Active => _active.Values;

    internal void Apply(FalloutImageSpaceModifier modifier) => _active[modifier.Form] = new(modifier, 0);
    internal void Remove(FalloutFormKey form) => _active.Remove(form);

    internal IReadOnlyList<FalloutImageSpaceModifier> Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var expired = new List<FalloutImageSpaceModifier>();
        foreach (var (form, active) in _active.ToArray())
        {
            var elapsed = active.ElapsedSeconds + seconds;
            if (active.Source.Animated && elapsed >= active.Source.Duration)
            {
                _active.Remove(form);
                expired.Add(active.Source);
            }
            else _active[form] = active with { ElapsedSeconds = elapsed };
        }
        return expired;
    }

    internal FalloutImageSpaceFrame Compose(FalloutImageSpace source, float? gameHour = null, FalloutDoubleVisionPhase? phase = null,
        FalloutImageSpaceModifier? presentationModifier = null, IReadOnlyList<FalloutWeatherImageModifier>? weather = null,
        bool sunlightOwned = false, bool skyOwned = false)
    {
        if (gameHour is { } hour && !float.IsFinite(hour)) throw new ArgumentOutOfRangeException(nameof(gameHour));
        // Static menu backgrounds temporarily apply an IMAD while drawing the
        // captured world. That request must never enter saved gameplay state.
        var activeModifiers = presentationModifier is null ? _active.Values.ToArray() :
            _active.Values.Where(active => active.Source.Form != presentationModifier.Form)
                .Append(new(presentationModifier, 0)).ToArray();
        var traits = new float[33];
        if (source.SkinDimmer is null)
        {
            Array.Copy(source.RawTraits, traits, 14);
            traits[14] = 1;
            Array.Copy(source.RawTraits, 14, traits, 15, 18);
        }
        else source.RawTraits.CopyTo(traits, 0);
        traits[25] = source.Cinematic.X; traits[26] = source.Cinematic.Y;
        traits[27] = source.Cinematic.Z; traits[28] = source.Cinematic.W;
        var multipliers = new float[21];
        var adds = new float[21];
        var tintNumerator = new Vector3(source.Tint.X, source.Tint.Y, source.Tint.Z) * MathF.Max(0, source.Tint.W);
        var tintWeight = MathF.Max(0, source.Tint.W);
        var strongestTint = tintWeight;
        var fadeNumerator = Vector3.Zero;
        var fadeWeight = 0f;
        var strongestFade = 0f;
        var blurRadius = 0f;
        var doubleVision = 0f;
        var unbound = new HashSet<string>(StringComparer.Ordinal);
        // Native weather instances first aggregate straight RGBA. Only that
        // aggregate enters the ordinary premultiplied/max-strength accumulator.
        // Applying the ordinary maximum independently to weather slots loses
        // tint when two slots select the same source during a time blend.
        var weatherTint = Vector4.Zero;
        var weatherFade = Vector4.Zero;
        foreach (var contribution in weather ?? [])
        {
            var modifier = contribution.Source;
            var strength = contribution.Strength;
            if (!float.IsFinite(strength) || strength < 0 || strength > 1 || modifier?.Animated == true)
                throw new NotSupportedException("Weather modifier percentage or instance lifetime is unbound.");
            if (modifier is null)
            {
                // The reserved default has identity scalar curves and straight
                // white RGB with zero alpha. Its RGB still participates when
                // another weather slot contributes nonzero tint/fade alpha.
                weatherTint += new Vector4(1, 1, 1, 0) * strength;
                weatherFade += new Vector4(1, 1, 1, 0) * strength;
                continue;
            }
            for (var index = 0; index < 21; index++)
            {
                var multiplier = FalloutImageSpaceModifier.Sample(modifier.Multiply[index], 0, 1);
                var add = FalloutImageSpaceModifier.Sample(modifier.Add[index], 0, 0);
                multipliers[index] += (multiplier - 1) * strength;
                adds[index] += add * strength;
                if (strength > 0 && index is not (4 or 17 or 18 or 19 or 20) && !(sunlightOwned && index == 11) && !(skyOwned && index == 8) && (multiplier != 1 || add != 0))
                    unbound.Add($"{modifier.Form}/{index}IAD");
            }
            weatherTint += FalloutImageSpaceModifier.Sample(modifier.Tint, 0, new Vector4(1, 1, 1, 0)) * strength;
            weatherFade += FalloutImageSpaceModifier.Sample(modifier.Fade, 0, Vector4.Zero) * strength;
            foreach (var effect in modifier.Effects)
                if (strength > 0 && FalloutImageSpaceModifier.Sample(effect.Value, 0, 0) != 0)
                    unbound.Add($"{modifier.Form}/weather-{effect.Key}");
            if (modifier.RadialFlags != 0 || modifier.DepthUsesTarget) unbound.Add($"{modifier.Form}/target");
            if (modifier.IntroSound is not null || modifier.OutroSound is not null) unbound.Add($"{modifier.Form}/audio");
        }
        var weatherTintStrength = MathF.Max(0, weatherTint.W);
        tintNumerator += new Vector3(weatherTint.X, weatherTint.Y, weatherTint.Z) * weatherTintStrength;
        tintWeight += weatherTintStrength;
        strongestTint = MathF.Max(strongestTint, weatherTintStrength);
        var weatherFadeStrength = MathF.Max(0, weatherFade.W);
        fadeNumerator += new Vector3(weatherFade.X, weatherFade.Y, weatherFade.Z) * weatherFadeStrength;
        fadeWeight += weatherFadeStrength;
        strongestFade = MathF.Max(strongestFade, weatherFadeStrength);
        foreach (var active in activeModifiers)
        {
            var modifier = active.Source;
            var time = modifier.NormalizedTime(active.ElapsedSeconds);
            for (var index = 0; index < 21; index++)
            {
                var multiplier = FalloutImageSpaceModifier.Sample(modifier.Multiply[index], time, 1);
                var add = FalloutImageSpaceModifier.Sample(modifier.Add[index], time, 0);
                multipliers[index] += multiplier - 1;
                adds[index] += add;
                if (index is not (4 or 17 or 18 or 19 or 20) && !(sunlightOwned && index == 11) && !(skyOwned && index == 8) && (multiplier != 1 || add != 0))
                    unbound.Add($"{modifier.Form}/{index}IAD");
            }
            var tint = FalloutImageSpaceModifier.Sample(modifier.Tint, time, new Vector4(1, 1, 1, 0));
            var tintStrength = MathF.Max(0, tint.W);
            tintNumerator += new Vector3(tint.X, tint.Y, tint.Z) * tintStrength;
            tintWeight += tintStrength;
            strongestTint = MathF.Max(strongestTint, tintStrength);
            var fade = FalloutImageSpaceModifier.Sample(modifier.Fade, time, Vector4.Zero);
            var fadeStrength = MathF.Max(0, fade.W);
            fadeNumerator += new Vector3(fade.X, fade.Y, fade.Z) * fadeStrength;
            fadeWeight += fadeStrength;
            strongestFade = MathF.Max(strongestFade, fadeStrength);
            foreach (var effect in modifier.Effects)
            {
                var value = FalloutImageSpaceModifier.Sample(effect.Value, time, 0);
                // Scalar blur is the strongest active request, not an additive
                // camera filter. Each modifier still owns its gameplay clock.
                if (effect.Key == "BNAM") blurRadius = MathF.Max(blurRadius, value);
                else if (effect.Key == "VNAM" && gameHour is not null && phase is not null) doubleVision = MathF.Max(doubleVision, value);
                else if (value != 0)
                    unbound.Add($"{modifier.Form}/{effect.Key}");
            }
            if (modifier.RadialFlags != 0 || modifier.DepthUsesTarget) unbound.Add($"{modifier.Form}/target");
            if (modifier.IntroSound is not null || modifier.OutroSound is not null) unbound.Add($"{modifier.Form}/audio");
        }
        for (var index = 0; index < 21; index++)
            traits[TraitIndices[index]] = traits[TraitIndices[index]] * (1 + multipliers[index]) + adds[index];
        var finalTint = tintWeight > 0 ? new Vector4(tintNumerator / tintWeight, strongestTint) : new Vector4(1, 1, 1, 0);
        var finalFade = fadeWeight > 0 ? new Vector4(fadeNumerator / fadeWeight, strongestFade) : Vector4.Zero;
        // The original effect's rotation is one turn per game hour. It reads
        // the shared game-time global, independently of the IMAD lifetime.
        var angle = phase?.Angle(gameHour.GetValueOrDefault()) ?? 0;
        var doubleVisionOffset = new Vector2((float)(Math.Cos(angle) * doubleVision), (float)(Math.Sin(angle) * doubleVision));
        return new(traits[4], new Vector4(traits[25], traits[26], traits[27], traits[28]), finalTint, finalFade,
            activeModifiers, unbound.Order(StringComparer.Ordinal).ToArray(), blurRadius, doubleVisionOffset, weather, traits[11], traits[8]);
    }
}
