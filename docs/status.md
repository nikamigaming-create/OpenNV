# Current product status

OpenNV is not yet a fully playable implementation of Fallout 1, Fallout 2,
Fallout 3, Fallout: New Vegas, or Tale of Two Wastelands.

## Working foundations

- Direct C# readers for Bethesda plugins, BSA archives, NIF, DDS, animation,
  audio, strings, cells, actors, collision, and selected gameplay records.
- Direct classic Fallout DAT, MAP, PRO, and FRM reading for bounded areas.
- New Vegas live opening state through stage 200 with source-resolved player
  inventory and validated cold Continue state.
- Complete active-CELL source-reference discovery with fail-closed runtime
  presence reporting. The owned Doc house, Wasteland persistent CELL, and
  Prospector Saloon are measured without named actor success paths.
- Canonical exact-byte telemetry packets, a loss-detecting shared-memory ring,
  typed comparisons, trace evidence, divergence frame retention, and diagnostic
  video planning.
- Bounded Fallout 1, Fallout 2, and Fallout 3 routes and synthetic contracts.
- C# Debug and Release builds, formatting checks, C# contract probes, desktop
  launcher tests, and Godot project loading.

## Incomplete systems

- Whole-campaign quest, dialogue, package, AI, combat, inventory, crafting,
  world streaming, audio, UI, and save semantics.
- Complete actor, creature, FaceGen, animation, material, lighting, effects,
  navigation, collision, and world coverage.
- Standalone Fallout 3 ordinary play beyond the bounded Vault 101 work.
- TTW world execution and shared campaign persistence.
- Portable implementations for required script-extender and JAM behavior.
- Integrated flat-route acceptance and physical OpenXR acceptance.
- Retail visual and behavioral parity across the complete denominator.

The active priority is synchronized retail/OpenNV observation followed by one
ordinary New Vegas route from the real front end through Doc Mitchell,
Goodsprings, Sunny Smiles, the Prospector Saloon, save, restart, and Continue.
See `docs/current-work.md` for verified measurements, exact gaps, execution
order, and the repeatable task handoff.
