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

The last validated runtime baseline is `b81cc2cc3e385176a43890078920a3affef5cb5f`.
It is on `main`, was pushed to `origin/main`, and passed the complete C#/Godot
gate with Godot 4.7.2 Mono.

- Canonical little-endian telemetry packets, exact-byte comparison, typed field
  deltas, a loss-detecting Windows shared-memory ring, traces, divergence frame
  retention, three-panel Godot review, and hash-bound video plans exist.
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
- The private WOW64 retail telemetry producer is not connected to the public
  shared-memory protocol. There is no complete synchronized retail/OpenNV run.
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

1. Connect the private WOW64 retail observer to packet v1 and prove ordered,
   loss-detecting retail packets without controlling retail state.
2. Add the diagnostic input duplicator and matched state-key join so ordinary
   player input drives comparable retail and OpenNV runs.
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
