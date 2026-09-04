# Current work

This is the canonical handoff for the next OpenNV task. `origin/main` is the
authoritative code state. Update this document whenever verified capability,
evidence, or the next implementation owner changes.

## Mission

Build one faithful engine path for Fallout: New Vegas, Fallout 3, Tale of Two
Wastelands, and associated plugins from legally owned files. The runtime reads
the winning plugin and resource graph into memory, creates authoritative C#
gameplay state, and presents it through Godot in flat and OpenXR modes.

Parity means the complete live denominator: every active source identity,
transform, actor, package, animation, quest, dialogue result, inventory change,
effect, sound, UI state, draw, simulation boundary, frame time, and final pixel.
No selected failure list can certify the game.

## Verified baseline

The current `origin/main` is the validated baseline. It passed the complete
C#/Godot gate with Godot 4.7.2 Mono. Resolve its exact identity with
`git rev-parse origin/main` rather than copying a stale commit into a new task.

- Canonical little-endian telemetry packets, exact-byte comparison, typed field
  deltas, a loss-detecting Windows shared-memory ring, traces, divergence frame
  retention, three-panel Godot review, and hash-bound video plans exist.
- Float32 telemetry preserves original IEEE-754 bytes, including signed zero
  and NaN payloads. Field mismatch reports retain both types and complete hex
  values. Unknown or different event identities cannot produce an exact-state
  result. RGB8/RGBA8 pixel comparison checks every channel without tolerance.
- `--parity-capture` connects native viewport readback to pre/post-draw
  telemetry. An ordinary OpenNV front-end launch retained 24 native RGB8 frames
  and 96 hash-checked packet/pixel/PNG files. This verifies capture plumbing
  only; the captured state was startup, with no matched retail gameplay.
- The reviewed private Win32 observer now feeds strict neutral snapshots through
  the public retail ingress into packet v1. A live owned FalloutNV.exe proof
  published three ordered gameplay packets carrying the engine millisecond
  timer, active CELL and attach state, player identity, and normalized player
  position in observe-only mode. Private RVAs and pointer layouts remain outside
  the repository.
- Distinct retail and OpenNV rings now feed a strict live FIFO join by exact
  state key and event ordinal. Producer gaps, ring overruns, wrong-engine
  packets, and bounded unmatched-state overflow fail closed; original packets
  can be retained in hash-validated traces with a JSON join report.
- OpenNV telemetry now reads an explicit authoritative current-CELL owner and
  player-root transform. Door streaming no longer reports the startup CELL
  after the active scene changes.
- The live active-cell registry discovers every reference from the decoded ESM
  graph. Missing runtime entities remain explicit divergence. Initially disabled
  references are observed as disabled rather than instantiated.
- The metadata-only Sunny actor path and its named appearance resolver were
  deleted. No actor can be counted as rendered through that path.
- Legal zero lighting-template FormIDs are decoded directly. The owned
  Prospector Saloon CELL now loads.
- Interior exits are enumerated from all active XTEL door references. The four
  owned Prospector Saloon door pairs resolve, including authored door scale.
- Actor model-front facing has a tested source-space basis and is applied during
  opening guide travel.
- The obsolete shot-state injection capture path and its runtime configuration
  were deleted. Matched comparison proceeds through live telemetry.

The verified official New Vegas stack contains 10 plugins, 628,395 winning
records, 44,517 CELL records, and 3,056,284 subrecords. The FalloutNV.esm
SHA-256 is
`50991d36804b7d1e70df1afd7471b72f0e29d1b456ee2516a9717c002564e7c1`.

Owned route measurements:

| Space | References | Enabled | Models | Lights | Enabled actors | Unpresented enabled references |
|---|---:|---:|---:|---:|---:|---:|
| Doc Mitchell house | 435 | 432 | 401 | 25 | 1 | 6 |
| Prospector Saloon | 461 | 454 | 426 | 24 | 3 | 4 |
| Wasteland persistent CELL | 6,093 | 4,935 | 4,262 | 5 | 868 | 672 |

These are source and runtime-coverage measurements, not gameplay or parity
passes.

## Current truth

- OpenNV is not a complete playable replacement for any supported campaign.
- The private WOW64 retail observer is connected to the public shared-memory
  protocol with an authoritative retail engine millisecond tick, CELL state key,
  and first player/world fields. The authoritative retail event ordinal remains
  unrecovered and zero, matched input is absent, and there is no complete
  synchronized retail/OpenNV run or final-frame identity.
- The observer now retains source Float32 bytes and the measured read interval.
  A fresh observe-only attachment reached retail but found no parent gameplay
  CELL. Live gameplay emission of the updated raw-bit fields remains to be run
  after the user loads a game. Polling does not observe events between samples;
  lossless packet transport therefore does not mean complete telemetry.
- The unverified actor experiment was removed from the active build and kept
  locally under `tmp/actor-development/unverified-ed84fbac352547aeb2092c1e1bf85b41`.
  Its skin/hair approximations, static pose, and missing face behavior are not
  implemented retail parity. Resume from observed source/runtime behavior.
- Active-cell identity coverage is live, but authoritative telemetry is still
  missing for many actor, bone, animation, package, quest, dialogue, inventory,
  effect, audio, material, draw, UI, and input owners.
- Generic full-body NPC/creature construction is absent from the direct cell
  path. Source actors therefore remain visible as missing runtime entities.
- Sunny Smiles package travel has a corrected facing contract, but VCG02
  dialogue/package execution and the source-authored rifle/ammunition handoff
  are not implemented.
- Exterior streaming, arbitrary interior entry, AI packages, navigation,
  combat, dialogue, radio, furniture use, effects, complete saves, FO3 ordinary
  play, and TTW campaign execution remain incomplete.

## Today’s execution order

1. Observe retail gameplay update, semantic-event, and render/present boundaries.
   Establish their actual ordering before publishing event identity. Never
   substitute a polling-sample counter for an observed event stream. Retain
   native retail frame bytes and associate them with those boundaries.
2. Add the diagnostic input duplicator so ordinary timestamped player input
   drives the existing exact state-key/event-ordinal join in both games.
3. Wire complete live telemetry at authoritative owners, starting with actors,
   packages, animation, dialogue results, inventory mutation, quest stages,
   audio, UI, renderer submissions, and final frame identity.
4. Implement generic direct NPC/CREA construction from the owned record,
   race/head/equipment/skeleton/skin/animation graph. Do not add named actor
   paths.
5. Run Doc Mitchell house -> exterior -> Sunny Smiles -> Prospector Saloon.
   Fix every discovered engine divergence generically, including VCG02 rifle
   handoff, package facing and arrival, actors, patrons, radio, furniture,
   clutter, doors, dialogue, and persistence.
6. Produce aligned state reports, pixel differences, audio/event differences,
   and a few hash-bound side-by-side clips from the same matched run.
7. Apply the same owners to Fallout 3 and TTW rather than creating campaign
   replicas of New Vegas systems.

## Repeatable task loop

For every new task:

1. Read `AGENTS.md`, this file, `docs/architecture.md`, `docs/status.md`, and
   `docs/parity-telemetry.md`.
2. Verify `main`, `origin/main`, and worktree state.
3. Reproduce the next divergence from owned data or matched telemetry.
4. Trace source identity and runtime ownership. Remove any artificial reader or
   runtime restriction exposed by the owned corpus.
5. Implement one general capability with no proxy, named special case, or
   capture-only path.
6. Add deterministic synthetic proof and an owned-data audit.
7. Run the complete gate and any matched retail/OpenNV evidence required by the
   claim.
8. Commit and push directly to `main` without a PR.
9. Update this file with the new truth and next owner.

## Commands

```powershell
git fetch origin main
git status --short
git rev-list --left-right --count origin/main...main

dotnet run --project .\contract-tests\FalloutPluginRuntimeProbe\FalloutPluginRuntimeProbe.csproj -c Release -- 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'

.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

A verified recovery bundle for the pre-handoff engine baseline exists at
`D:\code\OpenNV-recovery-20260904.bundle`.

## New-task prompt

Use this when starting a fresh task:

> Read AGENTS.md and docs/current-work.md completely. Continue from clean
> origin/main. Execute the next telemetry-proven OpenNV divergence as a general
> C#/Godot engine capability, add synthetic and owned-data proof, run the full
> gate, update docs/current-work.md, and push directly to main without a PR.
