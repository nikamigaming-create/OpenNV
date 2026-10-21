# Current work

Updated 2026-10-21. Priority: fully playable vanilla Fallout: New Vegas in flat
mode. Root owns this block without delegated agents. Development is greenfield:
prioritize working gameplay, not compatibility with old development saves.
Owned retail inputs remain read-only. Follow docs/implementation-plan.md.

## Active block

Branch: codex/fnv-sunny-shooting. Normal play completes Sunny's shooting lesson:
rifle handoff, R reload, three scripted bottle hits, objective completion and the
next lesson choice reach stage 25. Following Sunny to the first well and hearing
her instruction reaches stage 35; F5 completes. Private checkpoint:
local/fnv-wells/resume.json.

Two general runtime fixes are implemented:
- Linked-start patrols without an actor link are ineligible, so selection continues
  down the package stack. Casual patrol's weapons-unequipped flag uses the existing
  shared weapon policy. The two tutorial geckos now select their next source
  package, Sandbox 074fe6, which remains unimplemented and visibly reported.
- Physical placed PGRE/PMIS records use the existing object continuation owner.
  Restore previously rejected their captured state and prevented stage-35 Continue.
  The actual PGRE model passes capture/JSON/world restoration; normal Continue now
  loads the checkpoint successfully. Actor physics remains excluded from this owner.

During the live check, gecko 10a1fe acquired and pursued the player, landed melee
hits and reduced health to zero, opening normal death recovery. This establishes
that combat runs independently of the missing ambient package, not correct
encounter completion. No encounter win, looting or cooking completion is claimed.
The session exited cleanly. Recording remained off; temporary screenshots were
inspected and deleted.

Selected synthetic/owned patrol and native placed-grenade checks pass. The full
combined runtime gate and diff check pass. Live gameplay was closed before the
gate. Publish/merge this checked block, then start a fresh branch from synchronized
main. No PR exists for this block yet.

## Next gameplay owner

Implement general source Sandbox activity selection/execution for NPCs and
creatures, including native movement, authored idle/furniture use, occupancy,
interruption and continuation. Static animation-object attachment continuation
already works; it does not make autonomous work functional. Continue the actual
first-well fight, loot, gathering and campfire lesson, then Primm/ED-E and the Strip.
Use ordinary input and source quest consequences; do not force success states.

The remaining failure inventory is in docs/status.md and
docs/implementation-plan.md. Lockpicking, held-object saves, multiple combat and
script owners, rendering, audio and complete campaign progression remain open.
Bottles remain visibly intact after hits; physical impact response and matched
retail firing are unverified. No whole-game or retail parity completion is claimed.

## Preserved separate work

FO1/FO2 work remains separate. PR #242 (codex/classic-script-audio) is unrelated
and must be preserved. Physical FNV VR package g remains rejected by the user.
