# Open Nevada desktop launcher

The Open Nevada launcher is a first-party Electron application, designed as a
native desktop shell for Windows, macOS, and Linux. It deliberately does not
reuse another project's product language: its visual system is the Open Nevada
atlas, and legal provenance is kept in [NOTICE.md](../NOTICE.md), not in the
player-facing identity.

## Product contract

The launcher has exactly four top-level game choices. TTW is an edition under
New Vegas and Fallout 3, because it is a combined profile rather than a fifth
game button. The four choices and two TTW edition routes resolve to five
isolated character/save boundaries:

| ID | Path | Save boundary |
| --- | --- | --- |
| `fallout1` | Fallout 1 | One Vault Dweller state shared by hex/FPS and later OpenXR presentation adapters |
| `fallout2` | Fallout 2 | One bounded premade, modified, or custom Chosen One save cold-restores source/custom state and current Map 3 transform/mode; future FPS/OpenXR adapters must consume that same authority |
| `newvegas` | Standalone New Vegas | Mojave-only character |
| `fallout3` | Standalone Fallout 3 | Capital Wasteland-only character |
| `ttw-fo3` | TTW · Fallout 3 opening edition | One combined-world character/save boundary; bounded records-only FO3 opening contract, launcher-disabled |
| `ttw-fnv` | TTW · New Vegas opening edition | The same combined-world character/save boundary; no effective-stack Doc profile/runtime, launcher-disabled |

JAM is a profile layer for `newvegas` and the TTW editions, not another game choice. It can
be added later, but the launcher warns that a save using it must continue to use
it.

Every top-level game uses the same compact **FPS / Hex / VR** mode row. A mode
is clickable only when that campaign's registered profile and runtime manifest
both admit it; unfinished modes remain visible and disabled instead of moving
to an unrelated toggle or disappearing. TTW stays in the Edition selector and
JAM stays in the mod control.

The Windows default is a compact 680x480 logical-pixel window. At that width the
four game choices stay visible as one 2x2 grid, with the selected game's mode
buttons and Play action immediately below; setup and compatibility details stay
secondary. This avoids the near-full-screen result produced by the former
880x560 default on a 175% DPI desktop.

The portable contract is implemented in
[`desktop/src/contract.mjs`](../desktop/src/contract.mjs). It is deliberately
separate from the current Windows PowerShell bridge, so platform-specific
runtime launches cannot redefine product or save semantics.

New Vegas source selection is explicit. The contextual **Choose New Vegas
Data** action accepts either the legal game root or its `Data` directory,
requires a real `FalloutNV.esm` beginning with a `TES4` record, and registers
that read-only directory as layer zero of `opennv-mod-stack/v2`. It inventories
top-level ESM/ESP/BSA files by name, byte length, and last-write time without
extracting or hashing multi-gigabyte archives. Launches pass the sealed manifest
path, manifest SHA-256, stack ID, and campaign to Godot; the runtime resolves
the primary Data root from that manifest and rejects any mismatch. There is no
prepared-cache argument or legacy-cache
fallback on the New Vegas route. The final process-invocation boundary checks
the composed command as well: any native source-stack launch using a Python
executable, a `.py` helper, or `--cache-root`/`--reuse-cache` is rejected before
the child process starts. Legacy non-native evidence routes retain their
existing arguments and are outside that guard.

The same invariant covers every active standalone game card. Fallout 1 and
Fallout 2 pass only a sealed owned-install profile plus presentation/save
state; standalone Fallout 3 and New Vegas pass only their read-only Data root,
sealed loose/BSA stack identity, and save path. The launcher has no active
FO1/FO2/FNV/FO3 cache-path constructor and does not create, read, or pass a
prepared-content cache while composing or spawning those launches.

Some historical evidence contracts remain deliberately checked in but are not
launchable game routes. The disabled TTW opening proof retains its
`cacheCompatibilityId`/`cacheRoot` identity boundary; the legal-asset preparer,
classic-Fallout presentation compilers, and their proof/audit tools remain
offline evidence utilities. They are not reachable from Play on any of the
four standalone cards, and the native invocation guard rejects their arguments
if they are accidentally mixed into a standalone launch.

The retained runtime-only entry points are `--data-root`/`--reuse-cache` with
`LegalAssetPreparer`, `--fo1-hex-scene` and the FO1 campaign proof inputs,
`--fo2-temple-cache` with its proof/transition inputs, the old `--fo3-profile`
opening contract, and `--ttw-fo3-opening-profile`. They remain executable for
historical differential and audit work, but the desktop launcher constructs
none of them for FO1, FO2, FNV, or standalone FO3 Play.

## Runtime manifest and portability

The Electron shell reads `runtime-manifest.json` from a selected OpenNV runtime
folder on every platform. The manifest declares its Godot version,
capabilities, campaign readiness, launch eligibility, and platform executable.
The launcher never infers playability from a folder name or a bridge script.

The checked-in runtime declares the New Vegas owned menu/intro/Doc Mitchell
house route, its source-bound HUD/Pip-Boy runtime shell, plus its production Goodsprings active set and reciprocal Doc
Mitchell house/exterior exit, the bounded Fallout 1 Vault 13/V13ENT Hex/FPS
route, and the bounded Fallout 3 owned-profile menu/intro/CG00 source route
through persistent stage 90 inside the bounded owned birth-room flow. Fallout 2 is a
fourth visible game card whose owned DAT2 installation and exact Map 126/Map 3 source
graph can be admitted. Its bounded Hex route selects the owned
Narg, Mingan, or Chitsa premade, applies source stats/biography/portrait and
also exposes source-backed Modify/Create for name, sex, age, and exact SPECIAL.
Modify retains source tags/traits; Create leaves them unselected. Each exact
Narg/Mingan/Chitsa panel has one Portrait/Live 3D toggle. Live 3D uses the same
true 3D humanoid path as the Map 3 gameplay actor: an admitted owned FNV
full-body donor over authoritative GCD/FRM identity. Missing or incompatible
donor input fails closed; no procedural, FRM-player, silhouette, or standee
fallback is admitted, and the donor is not a parity claim. Confirm hands the selected
state to the grounded Map 3 player at tile 28707. An atomic version-12 save cold-restores the character mode,
source basis, custom state, tile, facing, transform, bounded modes, and the exact
Map 3 exit-to-ARTEMPLE Map 126 arrival plus the bounded source-identified
Villager HP/AP/defeat and exact nested-Spear loot state. The Temple HUD exposes
player HP/AP, deterministic adjacent melee, and the bounded inventory. An
alternative exact tagged-Speech Cameron route keeps Klint alive and reaches live
ARVILLAG input/save. The routes do not merge or infer a dead-guardian shortcut;
their retained hashes and boundaries are in the
[canonical FO2 branch ledger](evidence/fo2-first-slice-branch-ledger.md). The
launcher enables Hex after the owned DAT2 profile validates; FPS and VR remain
disabled. The non-source opaque Temple wall proxy is removed while
the owned wall FRMs and source-derived collision remain; Tag/trait editing,
target AI/turns, general scripting/combat/inventory, classic fixed-Y
composition, campaign-wide state, custom face/hair/skin editing, deterministic
custom portrait generation, and parity
remain open. The Fallout 3 bounded native opening reads its registered source
stack directly; Escape and the visible Skip action enter the same CG00 state.
**Set up Fallout 1** now selects the legally owned install folder itself. The
launcher validates and hash-binds `master.dat`, `critter.dat`, and the sealed
loose `DATA` inventory into user-profile metadata; it does not select or create
`hex-scene.json`, `character-start.json`, extracted FRMs, or another content
cache. Launch passes only `--fo1-owned-profile`, the selected presentation, and
an isolated Vault Dweller save path. Godot validates the DAT1 indexes in place
and reads the complete real V13ENT object graph and MAP-to-PRO-to-FRM closure in
memory. The Hex button now opens a bounded direct presentation containing 7,549
source floor patches from 57 FRMs and 1,100 exact static objects sharing 106
decoded FRM/rotation resources. Every nonvisual top-level record is transported
as source metadata: 351 Scroll Blockers, 20 Exit Grids with exact destinations,
one Security Door with its raw instance word, and 22 objects matched to live MAP
script records. Two further live scripts are retained without object bindings;
two nested inventory records are not world placements. There are zero
unclassified top-level objects. The 351 Scroll Blockers and closed Security Door
now provide 352 source-hex collision shapes; adjacent activation opens the
unscripted door. Five fully resolved Exit Grids expose the exact Map 6 / tile
17695 / elevation 0 / rotation 0 destination tuple, while 15 world-map sentinels
remain metadata-only. The interaction runtime is bound only to the launcher's
isolated save path and does not emit content or cache files. Script execution,
destination loading, general input/gameplay, first-person, and OpenXR remain
explicitly deferred and cannot silently fall back to the older prepared evidence
route.
The Fallout 1 OpenXR adapter has simulator coverage but remains
launcher-disabled and has no physical-headset acceptance. Fallout 3's current
early-birth implementation selects hash-bound KFs from exact source `PACK`
sections and composes the sampled `Camera1st` skeleton node through its parent
chain without a guessed `NiCamera` axis flip. All three presentation buttons
remain disabled because the current toddler proof auto-steers and lacks
ordinary configured-input/retail differential evidence. TTW-FO3 has a bounded
records-only compiler/executor, while TTW-FNV lacks an effective-stack Doc
profile/runtime; both editions remain disabled. JAM remains dependency- and
portable-semantic-gated, native DLLs are never loaded, and every full-campaign
readiness claim stays false.
The New Vegas UI shell binds the installed HUD/STATS/ITEMS/DATA XML graph,
selected owned bitmap fonts and textures, and the authoritative campaign
snapshot. Flat HUD, ITEMS, and DATA use selected source rectangles; STATS uses
the verified ITEMS frame until its remaining Gamebryo rectangle expressions are
implemented. Complete tile interaction and retail-pixel parity remain unpromoted
and do not change the route readiness flags.

Fallout 2 profiles can be registered with the existing offline profile tool or
launcher setup flow. The profile hashes `master.dat`,
`critter.dat`, and `patch000.dat`, verifies each DAT2 footer and directory, and
records a neutral source/index identity. The launcher rechecks those three
hash-bound files on every read, then starts the bounded native Map 3 route from
that profile. It neither extracts nor copies archive members and does not pass
a presentation-cache path to Godot.

TTW and JAM manifests are auto-detected from
`%LOCALAPPDATA%\OpenNV\profiles\ttw-profile.json` and
`%LOCALAPPDATA%\OpenNV\profiles\jam-profile.json`, or selected with the small
setup buttons beside their edition/module controls. The launcher verifies their
hash-bound inputs and reports not installed, changed, registered/runtime
pending, or ready. It passes only verified contract paths and identities and
never executes an extender DLL. A mod route remains disabled until both its
generated profile and the runtime manifest explicitly report compatible
semantics.

The **Mods and compatibility** panel also exposes **Install local mod ZIP** and
**Add mod folder** after the owned Data root is registered. The ZIP installer
extracts only ordinary stored/deflated files into an app-owned per-mod folder,
records the source archive hash and install metadata, rejects traversal, links,
special files, encryption, unsupported compression, corrupt CRCs, and an
existing identical destination, then appends that folder as the highest-priority
source layer. A single outer `Data` directory is stripped. 7z, automatic
downloads, scripted FOMOD choices, and native plugin execution are not claimed.
The panel visibly lists managed layers low-to-high and supports enable/disable
and priority changes. Those actions regenerate the same sealed source contract,
so the new stack identity receives its own save directory. Uninstall deletes
only a verified launcher-owned Gate Vortex install; external deployed and
manager-owned folders remain read-only and return an explicit refusal.
New Vegas and standalone Fallout 3 store independent catalogs, private install
roots, sealed stack identities, and stack-keyed saves. Fallout 1/2 controls show
their exact direct-source blocker because their current DAT contracts cannot
admit ordered external loose roots or archive replacement safely.
Each manual folder selection appends one read-only mod folder
to the New Vegas source stack at the next highest priority. The launcher
records a sealed `opennv-mod-stack/v2` identity, effective top-level
plugin/archive metadata, and the exact ordered roots, and rechecks those paths
and fast metadata before every launch. Saves live below
`profiles/newvegas/stacks/<stackId>` so different source stacks cannot silently
share a character. This is the common profile boundary for future
MO2/Wabbajack, Vortex, Nexus Mods App, Thunderstore, TTW-installer, and manual
adapters. The launcher does not claim automatic downloads or FOMOD execution,
and it never executes xNVSE/JAM native DLLs; those behaviors require native
first-party runtime implementations.

**Set up TTW** also projects an already validated `opennv-ttw-profile/v1`
directly into this native source-stack boundary. The owned New Vegas Data folder
remains layer zero and each generated TTW root remains a read-only higher layer;
the registered plugin order and active plugin-associated BSAs are preserved.
Plugin bytes are hash-verified during TTW registration. Ordinary launcher state
refresh and launch then use the sealed stack's fast file metadata and hashed
small provenance inputs, so they do not repeatedly hash the complete TTW ESM
set. This makes TTW record/resource transport available to the native loader;
it does not enable either TTW edition or execute extender DLLs.

The bounded TTW Fallout 3 opening contract is separately auto-detected as
`ttw-fo3-opening-profile.json` beside the registered TTW profile, with
`OPENNV_TTW_FO3_OPENING_PROFILE` as a development override. The launcher
revalidates its exact effective-source namespace, plugin/save identity, intro
movies, and dedicated cache identity. The current FO3 producer consumes the
records-only effective-source entry point; archive/loose-member winners are not
connected to a world. The New Vegas edition has no equivalent effective-stack
Doc profile/runtime. Validation therefore enables neither TTW edition.

Packaged builds prefer the executable declared by `runtime-manifest.json`.
Source development can set `OPENNV_GODOT` to a local Godot 4.7.2 executable and
connect the launcher to the repository `runtime` folder; the launcher then adds
`--path <runtime>` before the same campaign arguments. No retail or generated
asset is copied into the launcher or runtime package.

## Shared boot contract

Every promoted route must use the same coarse state machine:

```text
launcher profile -> verified local inputs -> legal/logo movies -> game menu
                 -> New/Continue/Load -> character opening -> first playable slice
```

Movie playback owns one cancel action. Escape and any visible Skip control must
converge on the same next state; skipping may not bypass character or campaign
initialization. Continue/Load restore the profile's canonical save. A proof-only
command-line bypass does not make a route launcher-ready.

Fallout 1 enables Hex Tactical and FPS over one authoritative Vault Dweller
state; its VR choice remains disabled. Fallout 2 enables only Hex for its
bounded premade-to-Arroyo slice; FPS and VR remain disabled. New Vegas enables FPS and its experimental software-gated
VR route while Hex remains disabled; OpenXR is not physical-headset accepted.
Fallout 3 keeps FPS, Hex, and VR disabled; its bounded menu/CG00 frontend is not
misrepresented as a first-person world. TTW-FO3 and TTW-FNV can share one
registered combined-world profile but neither edition can launch.

Presentation is selected independently from campaign and mod profile. Flat mode
launches Godot with `--xr-mode off`; experimental OpenXR mode launches with
`--xr-mode on -- --vr`. The runtime manifest must explicitly declare the OpenXR
mode launchable, and retains a separate `hardwareValidated` flag so a software
rig proof cannot be mistaken for a headset proof.

Each runtime port must pass the same gates before a release calls it playable:

1. isolated profile generation without source-folder writes;
2. new-character telemetry for each supported campaign;
3. extender capability tests for every promoted mod;
4. package launch and dependency checks on the release platform.

The launcher never claims a Windows native DLL is portable simply because its
archive was detected. The compatibility bridge replaces required behavior with
tested runtime contracts.

## Develop the shell

Install a current Node.js LTS release, then:

```powershell
cd desktop
npm install
npm test
npm run dev
```

The test suite validates the campaign boundary, JAM rule, and safe merging of
a platform runtime state into the product contract. `npm run package` builds a
desktop shell for the host platform.
