# Mod implementation

The requested product is select folders in the Godot launcher and play the
complete installed mod stack. JAM comes first; TTW follows. Full support includes
dependency behavior, configuration, UI, audio, animation, combat, ordinary input,
save/load and cold restart in flat and OpenXR. Unknown behavior remains visible
and prevents a completion claim.

## Authorized ten compatibility targets

The user authorized all ten targets, starting with JAM then TTW. Mods are additive
checkboxes under New Vegas, rather than mutually exclusive launch choices.
The default load order is automatic; manual changes belong under Advanced.
Each combination still requires its appropriate versions and patches.
Retail executable patchers such as NVAC and the 4GB patcher are excluded.

| Priority | Target | Behavior to support |
| --- | --- | --- |
| 1 | [Just Assorted Mods](https://www.nexusmods.com/newvegas/mods/66666) | All nine gameplay and HUD modules, settings and dependencies |
| 2 | [Tale of Two Wastelands](https://mod.pub/ttw/133-tale-of-two-wastelands) | Both campaigns, their DLCs, progression and travel |
| 3 | [Yukichigai Unofficial Patch](https://www.nexusmods.com/newvegas/mods/51664) | Winning bug-fix records and their runtime behavior |
| 4 | [JSawyer Ultimate Edition](https://www.nexusmods.com/newvegas/mods/61592) | Balance, survival rules and configuration |
| 5 | [Uncut Wasteland](https://www.nexusmods.com/newvegas/mods/56625) | Restored world content and placements |
| 6 | [The Living Desert](https://www.nexusmods.com/newvegas/mods/64623) | Travelers, patrols and world consequences |
| 7 | [NMC's Texture Pack](https://www.nexusmods.com/newvegas/mods/43135) | Texture replacement, source precedence and material presentation |
| 8 | [EVE](https://www.nexusmods.com/newvegas/mods/42666) | Weapon effects, impacts, explosions and deaths |
| 9 | [Nevada Skies](https://www.nexusmods.com/newvegas/mods/35998) | Weather and atmosphere |
| 10 | [New Vegas Bounties I](https://www.nexusmods.com/newvegas/mods/37310) | Authored quests, dialogue, combat and persistent outcomes |

Required xNVSE, JIP LN, JohnnyGuitar, kNVSE, UIO, MCM and other dependency
behavior belongs to each selected stack's acceptance, rather than occupying
target slots. Record exact versions, patches and dependencies per tested stack;
TTW's YUPTTW integration is distinct from standalone New Vegas YUP.

## Current result

The launcher has a searchable game/mod library, original generated desert art,
readable native controls and separate game, mod and dependency folder selection.
Selecting a mod opens its settings without replacing the active game. Checkboxes
enable several mods together. Nested extracted mod folders use the registered
New Vegas installation. Additional dependency and patch folders can be added;
priority changes and removal are available under Advanced. TES4 master declarations
are read directly and followed transitively, with malformed paths, cycles,
missing masters and malformed headers reported. These checks do not execute
native DLLs or prove their behavior.

The ordinary source owner reads the base plus all enabled mod/dependency folders;
later matching resources win. Same-named archives use the winning
folder; plugin-associated archives support spaced and compact dash suffixes.
An archive provenance hint cannot bypass the active resource winner. Mod
profiles use their explicit plugins, transitive masters and NAM activation,
without importing the unrelated retail plugins.txt. EVE selects one authored
DLC variant matching the available masters. These are read-only folder mounts;
no files are copied into the game installation.

Each effective combination and folder order has a separate save namespace.
Automatic ordering is independent of checkbox click order. Registrations,
enabled mods, ordering mode and explicit overrides survive restart,
including older v1 files whose record properties were written in PascalCase.
Combined TTW data is classified as New Vegas instead of standalone Fallout 3.
The existing unavailable gameplay gates remain until the real runtime passes.
This is launcher setup implementation, not the requested completed mod support.
Focused source/profile/stack contracts pass. All ten owned stacks and the combined
JAM + TTW + NMC stack open; this proves source loading only. The native launcher
check exercises three enabled mods, search and automatic order. All 5,407 winning
NMC textures decode, including two DDS files with partial authored mip chains;
their uploaded compressed bytes match GPU readback and their resources release.
This is not texture pixel parity or ordinary mod gameplay. The full required
repository publication gate passes; gameplay acceptance remains pending.

## Automatic load order

The shared C# planner follows authored master dependencies, master/regular plugin
partition priorities and reviewed ordering metadata for this collection. Stable
name ordering breaks unconstrained ties. TTW loads early, unofficial patches
follow it, frameworks precede ordinary plugins and Nevada Skies loads late.
Texture-only packages follow plugin packages; package patches follow their mod.
Unknown or cyclic requirements fail visibly. Manual ordering retains authored
master constraints and cannot bypass compatibility or gameplay gates.

Reviewed rules are grounded in the CC0
[LOOT New Vegas masterlist, revision 79b2bb6](https://github.com/loot/falloutnv/blob/79b2bb6db4ce560e8ecd80f66b375618d3e33405/masterlist.yaml).
OpenNV does not embed the LOOT engine or claim its entire database. The initial
coverage is the ten-target collection, its masters and known dependency/patch
plugins. Header ordering cannot settle every record or resource conflict.
Standalone YUP alongside YUPTTW is rejected; EVE's New Vegas plugin with TTW
requires a verified compatible variant. These are combination-specific issues,
not a one-mod-at-a-time restriction. Further compatibility metadata remains work.

## Dependencies and evidence

The author's [JAM 4.6 requirements](https://www.nexusmods.com/newvegas/mods/66666)
include the four main New Vegas DLCs, xNVSE, JIP LN, JohnnyGuitar, kNVSE,
Stewie Tweaks and UIO. The installed plugin's master declarations supply the
actual ESM dependencies. The local sample now contains all of those packages.
The shared interpreter now executes numeric NVSE assignments and eval conditions;
46 of its 52 source scripts still fail parsing. Accepted source still needs real
command and event owners. See [NVSE script runtime](nvse-script-runtime.md).
The earlier adapters only applied a speed multiplier and global time scale in
the old CellPlayer path. They do not provide AP behavior, animations, callbacks,
menus, complete module behavior or native-player integration.

[TTW](https://mod.pub/ttw/133-tale-of-two-wastelands) combines the campaigns and
their DLCs through the New Vegas engine. Its official installation guide links
the [required extension packages](https://thebestoftimes.moddinglinked.com/essentials.html).
TTW NVSE, xNVSE and the other installed extensions require their own runtime
behavior; a present library is not an implemented extension.

The private inventory contains JAM 4.6, TTW 3.4, YUP 13.9.1, JSawyer 5.6.3,
Uncut Wasteland plus NPCs 0.91b, Living Desert 2.7.3, NMC Small 1.0 with all-pack,
naval-chair and water-tower patches, EVE 1.19 Alternate, Nevada Skies 2281 Rework
and Bounties I 1.55 with Someguy Series 2.0. Dependency packages include xNVSE
6.4.9, JIP 57.30 and settings, JohnnyGuitar 5.28 and its all-tweaks preset, kNVSE
37, Stewie 10.00 and settings, UIO 2.30, MCM 1.5.1, TTW NVSE 3.3.3b and ShowOff
1.84 with settings. They remain private and are not distributed with OpenNV.
The xNVSE archive matches its author's release SHA256.

Private owned-data inspection is available through the existing probe:

```text
dotnet run --project contract-tests/FalloutPluginRuntimeProbe --configuration Release -- --audit-mod-install <mod-id> <mod-folder> <New-Vegas-folder> [additional-folder ...]
```

Reports retain source plugin hashes, active load order, archives, resolved files,
missing packages and script parser failures. JAM has 46 parser failures; TTW has
61 among 1,263 entry-plugin scripts. Neither count measures runtime execution.
Keep reports and all mod,
retail and derived files out of Git. Synthetic contracts cover dependency
resolution, transitive failures, cycles, mixed engine detection, malformed
headers, wrapped package discovery, DLC variant selection, loose/archive winners,
profile restoration, restart options and isolated save paths.

## Next owners and acceptance

1. Preserve the verified native launcher setup and source precedence across
   selected base, mod and dependency folders. Keep plugin winners, resources
   and saves bound to the same complete selection.
2. Implement the missing general script value/expression, function, event and
   persistent-state semantics used by JAM. Bind its dependency operations to
   real gameplay, UI and animation owners. Never report an unsupported call as
   successful or register a no-op extension.
3. Exercise every JAM module through ordinary input: dynamic crosshair, hit
   marker, hit indicator, objectives, hold breath, sprint, bullet time, weapon
   wheel and loot menu. Check configuration, interactions, AP, cancellation,
   inventory conservation and saved state, including flat/OpenXR continuation.
4. Complete TTW source layering, start selection, FO3/FNV progression and travel,
   dependency functions, quest state, UI, dialogue, combat and persistence.
   An isolated Vault 101 scene is not the combined campaign.
5. Run the required repository gate, owned-data audits, ordinary-input and
   matched retail checks before support claims. Physical headset acceptance
   remains separate from simulator evidence.
