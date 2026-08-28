# Open Nevada desktop launcher

The Open Nevada launcher is a first-party Electron application, designed as a
native desktop shell for Windows, macOS, and Linux. It deliberately does not
reuse another project's product language: its visual system is the Open Nevada
atlas, and legal provenance is kept in [NOTICE.md](../NOTICE.md), not in the
player-facing identity.

## Product contract

The launcher has exactly four top-level game choices. TTW is an edition under
New Vegas and Fallout 3, because it is a combined profile rather than a fifth
game button. Those choices resolve to five isolated character/save paths:

| ID | Path | Save boundary |
| --- | --- | --- |
| `fallout1` | Fallout 1 | One Vault Dweller state shared by hex/FPS and later OpenXR presentation adapters |
| `fallout2` | Fallout 2 | One future Chosen One state shared by hex/FPS/OpenXR presentation adapters; source registration only today |
| `newvegas` | Standalone New Vegas | Mojave-only character |
| `fallout3` | Standalone Fallout 3 | Capital Wasteland-only character |
| `ttw` | Combined TTW edition | One Capital Wasteland-to-Mojave character |

JAM is a profile layer for `newvegas` and `ttw`, not another game choice. It can
be added later, but the launcher warns that a save using it must continue to use
it.

The portable contract is implemented in
[`desktop/src/contract.mjs`](../desktop/src/contract.mjs). It is deliberately
separate from the current Windows PowerShell bridge, so platform-specific
runtime launches cannot redefine product or save semantics.

## Runtime manifest and portability

The Electron shell reads `runtime-manifest.json` from a selected OpenNV runtime
folder on every platform. The manifest declares its Godot version,
capabilities, campaign readiness, launch eligibility, and platform executable.
The launcher never infers playability from a folder name or a bridge script.

The checked-in runtime declares the New Vegas owned menu/intro/Doc Mitchell
house route plus its production Goodsprings active set and reciprocal Doc
Mitchell house/exterior exit, the bounded Fallout 1 Vault 13/V13ENT Hex/FPS
route, and the bounded Fallout 3 owned-profile menu/intro/CG00 route through
stage 62. Fallout 2 is a
fourth visible game card whose owned DAT2 installation and exact Map 126 source
graph can be admitted, but Hex, FPS, and VR are all disabled because no Fallout
2 rendered or interactive runtime presentation exists; the bounded exact Map
126 Godot scene-construction proof is not a launcher-ready mode. The Fallout 3
intro is converted locally to a hash-verified Theora cache during profile
registration; Escape and the visible Skip action enter the same CG00 state.
Fallout 1 Hex/FPS remain disabled
until the player uses **Set up Fallout 1** to select a generated
`hex-scene.json` and `character-start.json`; the launcher validates their
schemas and character-contract hash, stores only local paths under its user-data
folder, and supplies the exact runtime arguments plus an isolated Vault Dweller
save path. The Fallout 1 OpenXR adapter has simulator coverage but remains
launcher-disabled and has no physical-headset acceptance. Fallout 3 is enabled
by its registered owned profile only through stage 62; no Vault 101 runtime is
present. TTW runtime support is absent, JAM remains dependency- and
portable-semantic-gated, and every full-campaign readiness claim stays false.

Fallout 2 profiles are generated with `content/tools/fo2_profile.py` or
`scripts/Register-OpenNVFallout2.ps1`. The profile hashes `master.dat`,
`critter.dat`, and `patch000.dat`, verifies each DAT2 footer and directory, and
records a neutral source/index identity. The launcher rechecks those three
hash-bound files on every read. It neither extracts nor copies archive members.

TTW and JAM manifests are auto-detected from
`%LOCALAPPDATA%\OpenNV\profiles\ttw-profile.json` and
`%LOCALAPPDATA%\OpenNV\profiles\jam-profile.json`, or selected with the small
setup buttons beside their edition/module controls. The launcher verifies their
hash-bound inputs and reports not installed, changed, registered/runtime
pending, or ready. It passes only manifest paths and never executes an extender
DLL. A mod route remains disabled until both its generated profile and the
runtime manifest explicitly report compatible semantics.

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

Fallout 1 selects Hex Tactical or FPS over one authoritative Vault Dweller
state; its VR option remains disabled. Fallout 2 visibly lists Hex, FPS, and VR
but keeps every choice disabled. New Vegas exposes its original flat route and
an experimental software-gated OpenXR route; it has no Hex adapter and OpenXR
is not physical-headset accepted. Fallout 3 currently exposes only the flat
menu/CG00 profile and has no Vault 101 world FPS, Hex, or VR runtime. TTW's
separate combined-world profile can be registered but cannot launch.

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
