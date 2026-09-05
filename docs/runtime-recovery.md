# Runtime recovery

## Required outcome

Restore the complete starting sequence and Sunny's tutorial quest through
ordinary input, with persistent state and cold Continue. The route includes
the opening movie, bed/chair camera, actor animation and dialogue, character
creation, Vigor and psychology interactions, farewell, travel outside and
through the saloon, and Sunny's source-defined tutorial progression.

All behavior must be resolved from winning owned records, resources and scripts.
No named-actor success paths, selected quest-stage tables, forced state changes,
or replacement animations qualify. The route remains incomplete.

## Existing first-party implementation to reconnect

The parent of migration commit `7140879` retains the earlier implementation.
The following C# owners also still exist in the current tree. Their presence
does not mean that the direct-data runtime invokes them.

| Behavior | Existing owners | Current direct-data gap |
| --- | --- | --- |
| Ordered stage/result execution | `GamebryoStageCommandExecutor`, `GamebryoResultCommandExecutor`, `OpeningQuestRuntime` | Full command dispatch, variables and persistent quest graph |
| Dialogue and choices | `GamebryoDialoguePlayback`, `OpeningQuestRuntime.Dialogue` | General conversation/choice conditions and result ownership beyond SayTo |
| Packages and travel | `GamebryoPackageSelector`, `GamebryoPackageTravel`, `OpeningQuestRuntime.OrdinaryActors` | Direct PACK contracts and runtime invocation |
| Furniture and actor pose | `GamebryoFurnitureSession`, `OpeningGuidePriorityAnimation`, `ActorAnimationPlayback` | Direct furniture markers, source KF layers, camera and root handoff |
| Facial animation | `FaceGenMorphController`, `FaceGenLipAnimation` | Live TRI/expression targets bound to direct meshes |
| Sunny/tutorial combat | `OpeningQuestRuntime.CombatActors`, `GamebryoRangedCombat`, `GamebryoCreatureCombatAi` | Winning quest/actor/weapon contracts connected to the direct world |
| Door continuity | `LazyLinkedCellRoute`, source door readers and portals | Current route restricts portal binding and restoration to one pair |
| Persistence | `OpeningQuestRuntime.State`, gameplay save owners | Current native save validates completed-opening state, not the full recovered quest graph |

The old resource preparation code is a source for recovering first-party
algorithms, not an authorized persistent runtime input. Port its general
record/resource relationships into C# instead of restoring removed launch inputs.

## Verified recovery and current failure

- Intact actors now use source BSDismember partition identities to hide severed
  caps while retaining ordinary body parts and torso sections. Synthetic tests,
  the owned outfit/glove audit, and native pixels confirm cap removal. The
  remaining pose, head/attachment and material defects are still visible.
- The product no longer supplies a Doc-specific dialogue-stage table. Stage
  scripts discover speech waits; the selected INFO's executed result owns the
  target stage. Synthetic arbitrary-quest tests and live progression through
  naming exercise this path.
- Every entered stage is retained in execution order, including immediate
  transitions. Source PlayIdle commands now resolve the winning IDLE/KF and
  reverse ANIO links instead of being silently skipped.
- A fresh native replay follows New, movie skip, audio completion and name
  confirmation through the mirror handoff to character creation. Source-linked
  ANIO transforms and declared scalar tracks bind. Head/eye aiming is unbound
  even though the HeadTrack parameter is delivered.
- Source player packages select their actual KF camera clips, including animated
  ancestors in the first-person skeleton. A new replay visibly exercises wake-up,
  sit-up and seated naming. Exit, other first-person targets, camera projection
  and matched clocks remain open.
- Versioned IMGS, encoded-domain SLS and independent alpha/falloff policies now
  reach native room presentation. Full HDR parameters, IMAD blur/fades, original
  creation UI/fonts, actor furniture simulation, loot/skull and pool interactions
  remain required. See current-work.md for the latest owners.
- Physical OpenXR and matched retail acceptance have not been run for recovery.

## Acceptance

Drive the complete opening and Sunny quest with ordinary input, observe the
source-owned changes, save, restart and Continue. Compare behavior and native
frames against retail. Run selected owned-data audits and the repository gate
before publication. Keep failures visible; do not replace the required outcome
with a component test or a plausible frame.
