using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutActorFormOverride(FalloutFormKey Form, string Sha256, int Value);
internal sealed record FalloutActorHairOverride(FalloutFormKey? Form, string? Sha256);

internal sealed record FalloutActorFaceGeometryOverride(FalloutFormKey ModelOwner, string ModelSha256,
    FalloutFormKey Race, string RaceSha256, string ControlsSha256, byte[] SymmetricGeometry, byte[] AsymmetricGeometry);

// Targets can be placed actors, the runtime player, or actor bases. Base faction
// changes deliberately have a different lifetime/scope from AddToFaction.
internal sealed record FalloutActorOverrides(FalloutFormKey Target, string SourceSha256,
    IReadOnlyList<FalloutActorFormOverride> Perks,
    IReadOnlyList<FalloutActorFormOverride> Factions,
    FalloutActorFormOverride? CombatStyle = null, bool IgnoreCrime = false, bool IgnoreFriendlyHits = false,
    FalloutActorFormOverride? Race = null, FalloutActorFaceGeometryOverride? FaceGeometry = null, float? Height = null,
    FalloutActorHairOverride? Hair = null);
