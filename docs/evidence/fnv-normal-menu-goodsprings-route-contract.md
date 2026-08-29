# New Vegas normal-menu Goodsprings route contract

Status: **historical bounded forward flat proof retained; current-source route
reaches the Prospector Saloon final approach and remains fail-closed**.

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
actor recipe closure. Cell scene v14 rejects the former primary-centered link
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
reacceptance still awaits a passing first-run/cold pair. It does **not**
prove reverse traversal, neighboring exterior-grid streaming/load-unload,
an uninterrupted New Game-to-saloon run, Sunny dialogue/package AI, visual
parity, integrated OpenXR acceptance, or a complete campaign.

## Current source boundary

The mass-zero articulated-convex gap is closed. One fresh full cache at
`D:\Builds\OpenNV-fnv-articulated-convex-cache-20260829-r1` completed in one
process and admitted all four compiler families. It contains 6,104 files /
1,019,974,001 bytes. The unchanged owned Data snapshot was 321 files, 48
directories, 9,875,907,799 bytes, with size/mtime digest
`b2e21cd1d34d9e9a5b62dc68790fb8e390bdaaf0a442d764260469cb270c3bfc`.

Ordinary stage-200 Continue reaches the owned menu, physical Pip-Boy setup,
Doc-house portal, and Goodsprings exterior. Gate `0010757e` activates through
normal Godot input, waits for its exact one-second source Open terminal, and
keeps both the moving solid leaf and static posts physical. The runtime accepts
floor-recovery contacts only when their horizontal remainder is zero, treats
intermediate NAVM shared-edge waypoints as bounded tolerance regions, and keeps
strict 0.18-metre direct sweeps for the final three waypoints. Non-door stalls
consume only the existing three-replan budget; wrong-cell and XTEL blockers
still fail immediately.

The latest run is
`D:\Builds\OpenNV-fnv-articulated-convex-route-acceptance-20260829-r10`.
It passed the gate, then accepted the Prospector Saloon waypoint-57 edge in X/Z
without completing its 0.257-metre rise. At final approach waypoint 62/64, the
player capsule was still 0.333 metres below and overlapping the upper landing of
the correctly placed authored collision for REFR `001055e0`. The player remained
1.304 metres from that waypoint and 4.889 metres from the portal. No first-run
report was emitted, so the validator, cold Continue, and video capture were not
run. This is the exact resume boundary, not route acceptance or parity.

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
