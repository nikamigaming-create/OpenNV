# OpenNV agent contract

OpenNV is a clean, first-party C# and Godot/OpenXR reimplementation that reads
a user-owned Fallout installation directly. Every task starts by reading
`docs/current-work.md`, `docs/architecture.md`, and `docs/status.md`.
The current user-directed implementation plan is `docs/implementation-plan.md`.

## Product objective

- Reproduce Fallout: New Vegas, Fallout 3, Tale of Two Wastelands, and supported
  plugin behavior from the complete winning ESM/ESP and loose/BSA data graph.
- Treat every loaded object, state transition, event, audio voice, UI element,
  draw, frame, and final pixel as part of the parity denominator.
- Use ordinary player input, authoritative gameplay state, and persistent saves.
  A capture-only scene or selected checklist is not game support.
- Keep one product path: owned files -> C# readers and runtime owners -> Godot.
  In-process indexes and decoded-resource reuse are expected; transformed retail
  assets are never a persistent launch input.

## Non-negotiable boundaries

- Retail ESM/BSA/NIF/DDS files are read-only inputs. Never commit, package,
  upload, or distribute Bethesda assets, saves, executables, or derivatives.
- OpenNV does not depend on OpenMW source or runtime. Reverse-engineering output
  must be reduced to implementation-neutral contracts; never paste decompiler
  output into OpenNV.
- C# owns formats, records, world state, gameplay, telemetry, and persistence.
  Godot owns presentation, input adaptation, and OpenXR integration.
- Flat and OpenXR share authoritative gameplay and save state. VR is a
  first-class product mode, not a later camera patch.
- Never hand-place named actors or props, create proxy or metadata-only actors,
  hide divergence with presentation, or add location-specific success paths.
- Remove artificial format and runtime restrictions when owned records disprove
  them. Implement the general engine behavior and add corpus-backed proof.
- Unknown binary layouts, unsupported behavior, missing runtime entities, and
  telemetry loss fail closed and remain visible as divergence.

## Development workflow

- `main` is the only working and publication branch. Do not create a PR or a
  feature branch unless the user explicitly changes this rule.
- Begin with a clean `main` equal to `origin/main`. Preserve unrelated user work
  if the tree is not clean.
- Select the next telemetry-proven divergence, trace it to source records and
  runtime ownership, implement the general capability, and add synthetic plus
  owned-data proof.
- Update `docs/current-work.md` whenever the verified state or next owner
  changes. Do not leave task history or obsolete priorities in this file.
- Never claim campaign, cell, actor, rendering, or parity completion from record
  counts, identity, transport, or a visually plausible frame.

## Retail comparison boundary

- Retail FNV is WOW64/x86. Private live observation uses the Win32 Ghidrust MCP
  in observe mode. Private addresses, binaries, and captures are never public
  repository inputs.
- The diagnostic input bridge may duplicate timestamped user input into both
  games. Retail measurements may be compared with OpenNV but never become
  OpenNV gameplay authority.
- Exact canonical bytes, semantic fields, event order, frame timing, audio, UI,
  and final pixels are independent evidence lanes. Every lane must agree for a
  parity claim.

## Required gate

Before pushing a runtime or claim change, run the selected owned-data audit and:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

The gate covers Release and Debug C# builds, formatting and analyzers, contract
probes, launcher tests, and native Godot project loading. Matched retail/OpenNV
evidence is additionally required before any parity claim.
