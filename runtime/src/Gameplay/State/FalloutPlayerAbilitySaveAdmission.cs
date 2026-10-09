using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// This receipt can only originate from the unchanged pre-owner schema. A
// malformed current save cannot acquire it by omitting its authoritative state.
internal sealed class FalloutPlayerAbilitySaveAdmission
{
    private readonly FalloutNativeCampaignState _original;
    private FalloutPlayerAbilityScriptsSnapshot? _initialEffects;
    private FalloutPlayerAbilitySaveAdmission(FalloutNativeCampaignState original) => _original = original;

    internal static FalloutPlayerAbilitySaveAdmission? FromOriginalHeader(FalloutNativeCampaignState original)
    {
        if (original.Schema != FalloutNativeCampaignSave.BeforeAbilityScriptsSchema) return null;
        if (original.PlayerSkillValues is not null || original.PlayerAbilityScripts is not null)
            throw new InvalidDataException("Earlier campaign schema cannot carry active-effect/skill state.");
        return new(original);
    }

    internal static void Require(FalloutNativeCampaignState state, FalloutPlayerAbilitySaveAdmission? originalAdmission = null)
    {
        var missingSkills = state.PlayerSkillValues is null;
        var missingEffects = state.PlayerAbilityScripts is null;
        if (missingSkills != missingEffects)
            throw new InvalidDataException("Player skill pools and ability locals require one complete saved authority.");
        if (state.Schema != FalloutNativeCampaignSave.ExpectedSchema)
        {
            if (!missingSkills) throw new InvalidDataException("Earlier campaign schema cannot carry active-effect/skill state.");
            return;
        }
        if (missingSkills)
        {
            var originalShape = state with { Schema = FalloutNativeCampaignSave.BeforeAbilityScriptsSchema };
            if (originalAdmission is not null && originalShape == originalAdmission._original)
                return; // Initial Read validation only, before any owner/state publication.
            throw new InvalidDataException("Current campaign save is missing player skill/effect authority.");
        }
        var skills = state.PlayerSkillValues!;
        var effects = state.PlayerAbilityScripts!;
        FalloutPlayerSkills.ValidateValues(skills); FalloutPlayerAbilityScripts.Validate(effects);
        if (effects.Player != skills.Player || effects.PlayerWinner != skills.PlayerWinner || effects.PlayerSha256 != skills.PlayerSha256)
            throw new InvalidDataException("Saved player skills and active effects have different source authority.");
        if (state.PlayerActorValues is { } actor &&
            (skills.Player != actor.Player || skills.PlayerWinner != actor.PlayerWinner || skills.PlayerSha256 != actor.PlayerSha256 ||
                skills.StatsOwner != actor.StatsOwner || skills.StatsWinner != actor.StatsWinner || skills.StatsSha256 != actor.StatsSha256))
            throw new InvalidDataException("Saved player skill/effect authority differs from the actual SPECIAL source owner.");
    }

    internal FalloutNativeCampaignState CompleteOriginalRead(FalloutNativeCampaignState validated,
        FalloutPluginStack records)
    {
        if (validated.Schema != FalloutNativeCampaignSave.ExpectedSchema || validated.PlayerSkillValues is not null ||
            validated.PlayerAbilityScripts is not null || validated.SaveCompatibilityId != _original.SaveCompatibilityId ||
            validated.PlayerName != _original.PlayerName || !validated.Traits.SequenceEqual(_original.Traits))
            throw new InvalidDataException("Pre-owner read receipt does not match the validated original campaign.");
        var source = FalloutPlayerActorValueSource.Read(records);
        // The original schema has no successful mutable player skill or effect
        // local producer. Publish that exact initial authority once from actual
        // owned source, before returning a current-schema state to any caller.
        var skills = new FalloutPlayerSkillValuesSnapshot(FalloutPlayerSkills.ValuesSchema,
            FalloutPlayerActorValues.PlayerReference, source.Player, source.PlayerWinner, source.PlayerSha256,
            source.StatsOwner, source.StatsWinner, source.StatsSha256,
            new Dictionary<int, FalloutPlayerSkillPools>(), []);
        var effects = new FalloutPlayerAbilityScriptsSnapshot(FalloutPlayerAbilityScripts.Schema,
            FalloutPlayerActorValues.PlayerReference, source.Player, source.PlayerWinner, source.PlayerSha256, 0, []);
        var current = validated with { PlayerSkillValues = skills, PlayerAbilityScripts = effects };
        Require(current);
        if (_initialEffects is not null) throw new InvalidOperationException("Original player ability admission was already published.");
        _initialEffects = effects;
        return current;
    }

    internal bool PermitsInitialEffects(FalloutPlayerAbilityScriptsSnapshot state) =>
        ReferenceEquals(_initialEffects, state);
}
