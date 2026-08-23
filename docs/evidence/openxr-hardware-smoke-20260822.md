# OpenXR hardware smoke — 2026-08-22

This records user-operated Oculus Link evidence. The private recordings are not
redistributable build inputs and are not committed.

## Run 1 — hard failure

- Recording SHA-256: `8b2496e75b41ca97d12c14efc050699d464e8f7b1a5ffb5616bbb4db3051048e`
- Duration: 18.368 seconds
- Runtime: Oculus OpenXR 1.207.0, OpenXR 1.1.54
- Result: stereo/head tracking rendered, but both controller nodes were inactive,
  the wrist HUD fell to the floor, all actions stayed zero, and eye height was
  too low.
- Root cause: `XRController3D.Tracker` used OpenXR top-level paths instead of
  Godot tracker names. The correct names are `left_hand` and `right_hand`.

## Run 2 — controls pass, game-content fail

- Recording SHA-256: `83de3870306534e4ec1355ac0c0d8cf661c74ebc87b8e27a71bb3317af980317`
- Duration: 91.733 seconds
- Live facts: both trackers active/tracked; left-stick values reached 0.99;
  right-stick snap threshold was crossed; grip opened authored doors; the 10mm
  fired nine times; B reloaded to 12 and reduced reserve from 12 to 3.
- Result: locomotion, snap turn, door activation, fire state, reload state,
  haptics, and save persistence worked. The run still failed as a game: no held
  weapon render/shot feedback, oversized HUD, no normal gameplay actors, no
  exterior transition, no jukebox interaction, and substantial material errors.

## Contract added after the recordings

- Eye height waits for 30 consecutive frames with both controllers tracked,
  then calibrates once to 1.68 metres.
- The action map exposes Oculus Touch and OpenXR generic-controller profiles and
  eight named actions including reload.
- The owned-master 10mm profile is `0000434f`, damage 22, clip 12; concrete
  `Ammo10mm` is `00004241` with one reserve magazine for the smoke route.
- A compact three-line wrist HUD replaces the full inventory wall.
- The held retail 10mm model uses its decoded `ProjectileNode` for muzzle
  feedback and is attached to the tracked right-hand aim pose.
- The no-headset `--vr-layout-proof` gate requires held weapon, muzzle feedback,
  wrist HUD, compact pixel size, loadout state, and the enabled saloon actor.

OpenXR remains experimental and `hardwareValidated=false`. Exterior streaming,
Sunny Smiles, Trudy quest enable state, retail weapon animation/audio, jukebox
gameplay, material parity, and Havok-style pool physics remain separate gates.
