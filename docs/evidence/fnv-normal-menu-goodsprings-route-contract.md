# New Vegas normal-menu Goodsprings route contract

Status: **historical bounded forward flat proof retained; current-source route
reacceptance pending**.

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
actor recipe closure. Cell scene v13 rejects the former primary-centered link
semantics and carries each owned XTEL arrival transform. The runtime aligns each
next space to its reciprocal door, gives each CELL its own collision layer, and
switches the player's active collision and CELL ownership when normal activation
applies the authored XTEL arrival. A separate diagnostic still exercises a
closed/open ray, projectile ray, and two-way capsule probe for each portal pair.
The actor report must contain Sunny exactly once with `InitiallyDisabled=false`
and `ProofEnabled=false`; initially disabled Trudy remains excluded.

The owned main-menu Continue button drives a completed stage-200 campaign save
into this composite through a Godot button signal. Configured Godot movement and
activation then take the forward route `00103df9` → `000daebb` → `00106185`
through doors `00103e61` → `00103e69` and `0010636f` → `0010618e`.
The retained route proof recorded campaign save v5 with saloon CELL `00106185`,
opened-container remaining counts, and the player transform. Current save v6
loads that state and adds source-derived Level/HP/AP/XP. A fresh
process emits the owned Continue button again and must restore the unchanged save,
active saloon identity, and equivalent transform without replaying a transition.
Neither phase uses Windows app control or injected foreground input.

This historical evidence proves only the bounded eagerly instantiated forward
flat route from a completed save. A later direct native proof establishes
current-plus-neighbor resource suspension. Current-source normal-route
reacceptance still awaits an admitted source-articulation cache. It does **not**
prove reverse traversal, neighboring exterior-grid streaming/load-unload,
an uninterrupted New Game-to-saloon run, Sunny dialogue/package AI, visual
parity, integrated OpenXR acceptance, or a complete campaign.

## Current source boundary

The 2026-08-29 compiler-family migration produced one admitted fresh cache and
one ordinary stage-200 Continue/Pip-Boy acceptance. The first route attempt then
correctly collided with authored exterior geometry after a stale loose NAVM
point. Current code advances only across capsule-clear route segments and allows
at most three bounded replans. The next attempt reached ordinary closed gate
`0010757e`; current code activates only the actual in-range non-XTEL
`DoorInstance` returned by the player collision and persists its open state.

That gate still blocked the capsule because its old generated model had flattened
the animated leaf and static posts. The owned source NIF contains independent
Open and Close controller sequences for target `BGate`; Close is not exactly the
reverse of Open. Static export and runtime now preserve a hash-joined
`opennv-controller-door-articulation/v1` contract so only the target leaf's
visuals and authored collision move while `BPosts` and the REFR stay fixed.

Exactly one fresh cache build was started for that schema at
`D:\Builds\OpenNV-fnv-articulated-door-cache-20260829-r1`. It failed closed
after 245.365 seconds on
`meshes\dungeons\nv_craftsmanhomesinterior\nvcraftsmanrmdooranimated.nif`:
the target owns a mass-zero convex collision body, but the current static
collision exporter admits only MOPP packed triangles and therefore could not
join that authored body. The partial cache has no `install-manifest.json`, is
not admitted, and was moved to the Windows Recycle Bin after its failure
evidence was retained. The owned Data tree remained unchanged at 321 files,
9,875,907,799 bytes, with size/mtime digest
`25abe0156faaaad8f831b0bfc33745dc9a35cb0da65ac91e77eaab1c0323efbb`.
No retry or Godot acceptance followed. The next slice is to represent this exact
authored convex collision without weakening the join, rebuild once into a new
unique cache, then run ordinary route and cold-Continue acceptance.

## Bound local acceptance

The 2026-08-28 v13/v4 owned-data run completed checkpoint stage 55, resumed the
authored dialogue/voice sequence to stage 200, then ran two fresh Godot
processes. The first emitted the owned Continue button signal and recorded the
two ordered forward transitions before ending in `GSProspectorSaloonInterior`.
The second emitted Continue against the resulting save, recorded no transition,
and restored position `[88.60192, -4.288307, -0.42332718]` with an equivalent
normalized rotation under the validator's `0.000001` tolerance. Both reports
passed their independent manifest-backed validator modes. Sunny appeared once
with `InitiallyDisabled=false` and `ProofEnabled=false`; that is enable-state
evidence, not a behavior or parity claim. The private cache, reports, and save
remain uncommitted; their binding hashes are:

- compiler source identity: `e920870a68f118d5cee1d49af523e50c053de97b6cd3131a85ec6288ff520314`;
- runtime configuration: `f3961312ecd86bd213d908e272325521749e8f548bb9f8178989b9eef1c4bb33`;
- install manifest: `1c2d7c43dc18d3fc8b1daf73e086c51bef04ab98f71baf224e33ee02cfac8259`;
- root v13 CELL scene: `0aa19bdc1a9215c714b6b61703d0a185b0bc35988039a7c8a5bbcafe47d3cc96`;
- linked exterior scene: `afeec4ab887516d9c3dcaf5a7509ef065125910a108331eda93daa41b7536af3`;
- linked saloon scene: `c8315118210c0131d8d1889a790950f89b85e79253b3f4e354b1df3089886bcb`;
- actor manifest: `bcc7ee8176d4a9188299cf5d8209cdf1c1c7f559b6d0cad65516d1ca64a303ea`;
- opening manifest: `d8dd2ef88c8b33820a2e936622e42d0ebc6248f71b733ff0af28f07bf1eacde0`;
- stage-55 checkpoint report: `8bc3b37bfce657994154a7c8785bed222e469167d13cc44b6d04369ff89ef367`;
- stage-200 resume report: `5a959d3ad381810a05038c822bd6d50a742b7af4640af257fe89a8837ee39e4d`;
- first-run route report: `c0136b476f7b6b046c333613dce72e936246c990dad8323386e6014dc1b31812`;
- cold-reload route report: `d64f3a40b2b92f897007a73576d06be0b8e4ea5b9c6cfccbaa5dfcf47911c661`;
- unchanged final v4 save: `8b88003ef43bb74c3c5c3cd733a946fcece16f76b633a118bd64a31e31560bc5`.

Component evidence remains in the
[opening campaign-state contract](fnv-owned-opening-campaign-state-contract.md)
and [Goodsprings linked-world contract](fnv-goodsprings-linked-world-contract.md).
