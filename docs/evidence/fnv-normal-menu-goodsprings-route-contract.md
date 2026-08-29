# New Vegas normal-menu Goodsprings route contract

Status: **current-source bounded forward flat route and cold Continue pass**.

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

This page retains the historical r25 route evidence. It is superseded for
active-set policy and actor grounding by the
[current-CELL route contract](fnv-route-active-set-contract.md), which reuses the
same admitted cache and proves first-run plus cold Continue with only the
authoritative CELL active. This evidence proves only the bounded eagerly
instantiated forward flat route from a completed save. It does **not**
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

The accepted pair is
`D:\Builds\OpenNV-fnv-articulated-convex-route-acceptance-20260829-r25`.
The runtime now treats authored packed Havok triangle soup as two-sided for body
motion, keeps interaction rays front-face-only, and requires the existing
0.18-metre vertical convergence alongside intermediate X/Z tolerance. Portal
setup restores each articulated door synchronously to its closed terminal before
sampling proof frames and linked-space alignment. Activation fails on a real
non-door collider; an empty ray can select only one facing portal and records
the exact selected source-door identity. The first process climbed the source-
backed saloon porch, resolved exterior door `0010636f`, recorded both ordered
XTEL transitions, saved in CELL `00106185`, and emitted
`OPENNV_FLAT_ROUTE_TRAVEL_PASS phase=first-run`. A second process emitted
`phase=cold-reload`, restored the saloon/player state, and recorded zero replayed
transitions. Both reports passed their manifest-backed validator modes. The
private evidence hashes are:

- first-run report: `8701da0500a9b7ca5620c81b1c53236d1898deeb2bf72e93021286be571908e9`;
- cold-reload report: `fca248ad7f36e2caa172940559b6f7136fab5412aedced90963d35e4eb1eb3bc`;
- resulting save: `c8f6765e215f6221814cd602ee944d8411285b5d331879b246d1e41d1f22298f`.

The r20 pair is superseded. It crossed the second portal only because a
non-door saloon-shell hit was allowed to fall through to proximity/facing
activation, and its report did not bind the selected door identity.

This is route acceptance, not reverse traversal, full campaign coverage,
OpenXR acceptance, actor behavior, or retail visual parity.

## Current active-CELL environment correction

Commit `62a4dfa` removes the root-CELL-global environment leak. Before that
change, the exterior movie background was exact RGB `(73,68,48)`, matching Doc
Mitchell CELL `00103df9` XCLL fog byte-for-byte after the player had already
entered Goodsprings. The runtime now owns one mutable `WorldEnvironment` per
loaded route and switches it with the authoritative current CELL. Interior
transitions restore that CELL's XCLL background/fog. Configured bounded clear-day exterior
mode resolves Goodsprings `000daebb` through WRLD `000da726` and CLMT
`0008809b` to its unique unconditional 100-percent WTHR `000ffc88`
`NVWastelandClear`, uses the source climate day boundary at hour 8, and renders
the verified owned atmosphere/cloud models with four bound decoded cloud
texture layers. Exterior surface shaders and directional lighting still use
the existing provisional compiled CELL adapter; this correction does not claim
a complete owned WTHR lighting application.

The no-rebuild native acceptance proof is
`D:\Builds\OpenNV-fnv-route-environment-acceptance-20260829-r4`. Its first-run
and cold-Continue reports pass the manifest-backed validators and record
the selected weather, hour, bound cloud-texture count, environment update sequence, and
both sky-model hashes. Commit `75a78ff` makes the route validator require that
exact environment scope, weather selection, update order, source hashes, and
bound texture count.
The sky-corrected visual capture and shareable copies remain at
`D:\Builds\OpenNV-fnv-route-sneak-peek-20260829-r3-sky`. The private artifact
hashes are:

- accepted first-run report: `4f94eae1182ef32d1e643dc351e627bb9ed288c351b14824e88b448358c2d449`;
- accepted cold-reload report: `7353410112de5c518fe7a52c64010f0cd0b5320b291725a51da909c7fa119f2f`;
- accepted resulting save: `40c047aac33c4a293798389ae2ec764de1d7ee480171c4b6c9885cab46d0cd81`;
- landscape MP4: `84462af39409b1b716dc775e314f61dc7470d39409e43ad7a5db41c885bb2012`;
- mobile MP4: `4d55a15b77b886cc23e7c3ffdd7f188c4cfb8ab1e01e6b2ab9adbc7615ae13b4`.

This correction does not yet preserve or mutate the referenced climate GLOB,
advance campaign time, select conditional/night weather, resolve the missing
night-sky assets, or prove retail camera/material/image-space parity.

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
