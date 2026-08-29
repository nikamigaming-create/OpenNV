# Fallout: New Vegas owned opening campaign-state contract

Status: **bounded opening handoff accepted; full campaign unproven**.

## Scope

This contract covers the first-party Godot route from a new game through Doc
Mitchell's authored opening quest and its handoff to the world. It uses only a
player's legally owned Fallout: New Vegas master and archives plus named OpenNV
runtime policy. It does not certify the later campaign, retail HUD or Pip-Boy,
JAM, TTW, combat, neighboring-CELL streaming, or visual parity.

## Compiled boundary

The promoted owned-data compile emits new-game flow schema v5 and command
contract v1. The observed canonical flow contains 275 commands spanning all 25
runtime-supported kinds. Its compiler and runtime checks require:

- exact count agreement between stage, dialogue, and psychology commands;
- a stable FormID and record type for every declared item, quest, global,
  owner, and placed-reference identity;
- rejection of unknown command kinds or ambiguous owned records;
- the same runtime-configuration schema and SHA-256 used by the content cache;
- owned scene-role, package, animation, INFO, voice/LIP, and NAVM joins; and
- no executable content FormIDs, scene coordinates, item values, or route
  waypoints.

## Persistent boundary

Campaign save schema v3 embeds opening-state schema v1. The opening payload
contains the current quest stage and completion state, player identity and
character choices, SPECIAL/tag/trait results, psychology and quest variables,
quest/global/objective state, achievements, inventory/equipment, destroyed and
enabled references, player controls, and player/guide transforms. Exact FormIDs
remain the persistence keys; editor IDs are retained as provenance rather than
used as guessed runtime substitutes.

At the authored autosave, the runtime derives a collision-free departure from
the current interaction, owned NAVM, and configured capsule. While the opening
remains active, configured player input is grounded on that NAVM. Completion
removes the opening-only navigation adapter and returns the player to normal
world collision.

## Acceptance

Acceptance is deliberately split across two Godot processes:

1. `checkpoint` starts a new game, drives the authored UI and configured input
   map, validates the unique autosave stage, writes the canonical incomplete
   save, and exits.
2. `resume` loads that exact save without `--new-game`, restores all opening
   state, follows the owned navigation/dialogue route, executes the closing
   command effects, and requires the owned completion stage with `Completed`
   true.

The canonical local run passed from stage 55 to stage 200 and preserved the
created character, quest lifecycle, globals, objectives, inventory/equipment,
achievement state, and seven-bit player-control vector. The completed-Continue
restore path validates that vector and maps its movement, look,
rollover-derived activation, and fighting bits through the same helper used by
the live stage transition, preventing the prior silent combat re-enable.
Pip-Boy visibility is restored separately; point-of-view and sneaking bits do
not yet have runtime consumers. The two-process report proves the
incomplete-save resume to completion. The 2026-08-28 normal-menu route acceptance
adds a completed-save load through the owned Continue button, configured flat
movement and activation through both forward XTEL links, and a fresh-process
Continue. Campaign save v5 persists saloon CELL `00106185`; the cold process
restores stage 200, the unchanged save, and the same player transform there.
This is active-CELL identity inside one preloaded bounded composite, not
independent CELL streaming or reverse-traversal acceptance. The reports record
source scene, configuration identity, save SHA-256, initial/final state
summaries, and that Windows app control and foreground input injection were not
used. Generated
cache, save, voice, LIP, and other commercial artifacts remain local and are not
committed.
