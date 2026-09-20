using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutActorFormOverride(FalloutFormKey Form, string Sha256, int Value);

// Targets can be placed actors, the runtime player, or actor bases. Base faction
// changes deliberately have a different lifetime/scope from AddToFaction.
internal sealed record FalloutActorOverrides(FalloutFormKey Target, string SourceSha256,
    IReadOnlyList<FalloutActorFormOverride> Perks,
    IReadOnlyList<FalloutActorFormOverride> Factions,
    FalloutActorFormOverride? CombatStyle = null, bool IgnoreCrime = false, bool IgnoreFriendlyHits = false);
