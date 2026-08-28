# New Vegas normal-menu Goodsprings route contract

Status: **bounded ordered composite; campaign streaming continuity pending**.

The default owned-data recipe compiles one hash-bound chain from the player's
legal `FalloutNV.esm` and archive stack:

1. Doc Mitchell house CELL `00103df9`, door `00103e61`;
2. the owned Goodsprings active exterior set, reciprocal door `00103e69` and
   saloon exterior door `0010636f`;
3. Prospector Saloon CELL `00106185`, reciprocal door `0010618e`, and enabled
   Sunny Smiles ACHR `00104e85`.

The v2 root recipe names the targets in order. Preparation requires each source
door in the immediately preceding scene, independently verifies the target
door's XTEL arrival, hashes every target scene and recipe, and aggregates the
actor recipe closure. Cell scene v12 rejects the former primary-centered link
semantics. The runtime aligns each next space to its reciprocal door, uses all
linked collision layers for movement, activation, and firing, and exercises a
closed/open ray, projectile ray, and two-way capsule probe for each portal pair.
The actor report must contain Sunny exactly once with `InitiallyDisabled=false`
and `ProofEnabled=false`; initially disabled Trudy remains excluded.

The owned main-menu Continue button drives a completed campaign save into
this composite through a Godot button signal without Windows control or injected
foreground input. This proves a normal-menu cold load into the bounded route.
It does **not** yet prove ordinary player-driven travel, active-CELL identity in
the save, saloon-location cold restoration, Sunny dialogue/package AI, visual
parity, neighboring CELL streaming, OpenXR acceptance, or a complete campaign.

## Bound local acceptance

The 2026-08-28 owned-data run completed checkpoint stage 55, resumed through
the authored dialogue/voice sequence to stage 200, then opened the normal menu
and emitted its Continue button signal. The resulting load reported four
enabled actors, two ordered reciprocal portals, and passing closed/open ray,
projectile, local-floor, and strict two-way capsule checks at each portal. The
private cache, reports, and save remain uncommitted; their binding hashes are:

- compiler source identity: `e9c68d487c6641cdf42e2123d18ed886984bd8ce8a08a9cbe60ba9caa3704ed9`;
- accepted runtime assembly: `bf8f0b1f1e0dd51f0614723f7bd9d964c9cd09666cbb6c9beb935ec1d3ec856a`;
- runtime configuration: `f3961312ecd86bd213d908e272325521749e8f548bb9f8178989b9eef1c4bb33`;
- install manifest: `b4872b2628faf2c06274ddb0df806f5e2c647cd59e477cdce986d82766dd8226`;
- root CELL scene: `334d7cba5936dfc5c98d90b5707aec6f97cebd40104a0439f5196eaf6718cd3f`;
- actor manifest: `0a54fb6c2ec1867fcdd0ba2a5c78434b5b97bd92b260063b733d681866b87aaf`;
- stage-200 resume report: `bb2b47da437ff38c5550cdf1d93be855c406aee910836de30da00e330a8ba9d4`;
- menu/route report: `42058d6908c21041c305f65f56cc722705ddac61490942ba4f106df2c29c7638`;
- post-route save: `452905be09d58018957ec1652500bbafad346325cf593e71b53bd7711a7ee60a`.

Component evidence remains in the
[opening campaign-state contract](fnv-owned-opening-campaign-state-contract.md)
and [Goodsprings linked-world contract](fnv-goodsprings-linked-world-contract.md).
