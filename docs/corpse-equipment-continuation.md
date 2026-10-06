# Existing corpse equipment continuation

Authoritative death retires the actor's obsolete pursuit iterator, waypoints and
door wait. It does not finish, cancel or replay a door's already-dispatched
reference event or source motion. Consumed route failures and the requested door's
identity, elapsed wait and error remain in a retirement receipt. Death does not call
`EndEngagement`, dispatch `OnCombatEnd`, select another package, reset attack
randomness or remove the stopped source procedure fault.

This owner persists the equipment that the current runtime actually presents.
It does **not** implement retail weapon drops or infer a dropped-object transform,
impulse, inventory transfer, sound completion or physics layout.

## Save v45

`FalloutActorCorpseEquipment` joins the existing independent ragdoll, inventory,
combat history and stopped-procedure owners. It retains:

- The raw actor root basis/origin and actual activity flags/revision.
- Source skeleton resource/hash and the raw components of every bone not
  published by the actual source ragdoll. Weapon and anatomical pack anchors
  therefore do not revert to a fresh skeleton pose.
- Each already-created combat/package weapon model's winning WEAP hash and
  model resource/hash, complete ordered native node/parent/source-block layout,
  raw local matrices, visibility, render layers and anatomical attachment bones.
  Hidden cached equipment stays hidden; visible equipment is not removed or
  replaced to make capture pass.
- The selected native weapon's existing `FalloutWeaponHandlingSnapshot`,
  including draw state, magazines, inventory/virtual-ammo policy and both random
  streams. Retained engagement handling must agree with that independent owner.

Capture and cold restoration never call weapon selection, `Equip`,
`CompleteReload`, condition wear, inventory removal or an attack text-key
dispatcher. Source death-item grants still occur once through the existing
authoritative death owner before native death admission.

Cold binding validates winning record identities, actual retained items,
ammunition policy/counts, source model hashes, the complete native graph and
residual bone coverage before publishing saved native poses. It rebuilds only
the previously recorded presentation owners. Shared alert state must agree with
the saved activity receipt; native restoration cannot write that shared owner.
The deferred native death rig remains mandatory before resident capture.

v44 and established earlier schemas remain readable without rewriting their
original files. Older labels cannot carry v45 corpse equipment fields. A legacy
corpse with retained weapon handling but no equipment continuation remains
refused; no missing presentation is inferred from inventory.

## Lifetime and refusals

Pending hitscan contacts, source hit-event admission, actual finite muzzle/effect
graphs, live projectiles, decals and effect faults remain separate capture gates.
Finite source audio is neither stopped nor marked finished at death. Normal
capture requires genuine completion; a stopped retirement candidate can retain
its already-proven exact native finite generations and immutable nonaudio copy.
All corpse capture/readiness callbacks must retire before that candidate can
commit, and independent corpse/ragdoll data must remain unchanged. It never
rereads a disposed model to infer readiness.

Capture still fails closed for an attachment's active internal model controller,
particles/billboards, independent skeleton/deformation, autonomous native owner,
dynamic body, or a source weapon visual channel outside transform/visibility.
These have no newly invented persistence/drop layout. A lost graph, changed
source binding, absent weapon, phantom loaded round or incompatible shared alert
owner also refuses. `CorpseEquipmentCaptureBlocker` and the stopped-pose diagnostic
identify the boundary. Original source/package/hit faults remain independent.

## Settled corpse item retirement

Ordinary container rows, quantities, Take All and source RemoveItem/RemoveAllItems
share one staged inventory transaction. Every item and both owners preflight
before inventory publication or native removal. Both actual inventories publish
before reversible equipment commits; native destruction follows all successful
commits. A later commit failure restores exact inventories/revisions, equipped
variants, source faults and native attachment parents/order without a success
notice or UI result.

The native corpse must have its actual settled source ragdoll, current capture
binding, no pending hit admission and every existing audio/effect/procedure/head
gate. Only attachment graphs whose actual source weapon disappears retire.
Unrelated cached weapons retain visibility and pose. Handling retires only with
its selected source weapon; ammo-only changes use the shared Reconcile owner
without drawing randomness, reloading or inventing carried rounds.

Retained off-cell equipment follows the same source-validated reconciliation
after native callbacks are gone and original bodies/hits/audio are settled.
Resident, stale, pending or opaque owners refuse. Empty equipment stays empty
through current/tree retirement/cold state; v45 needs no proxy or new schema.
Living presented-equipment removal, partial equipped stacks and replacement/
unequip/reset presentation remain separate boundaries. This is not drop physics,
living weapon reselection, armor replacement or whole-loot/retail acceptance.

## Focused verification

The existing pure probe selector is:

```powershell
dotnet run --project .\contract-tests\ReferenceScriptContractProbe\ReferenceScriptContractProbe.csproj -- --corpse-equipment-contracts
```

It covers source-bound current/cold/retirement receipts, raw node/anatomical
binding negatives, missing/duplicate owners, item/condition/carried/virtual-ammo
conservation, actual source hit admission, future fields under a legacy label,
immutable finite-retirement copies, stale native bindings and unchanged stopped
corpse contracts. Synthetic fixtures are created under the project-local
`local` directory and deleted in `finally`. Synthetic Finished receipts test the
receipt contract only; they are not evidence of native audio completion.

The owned native audit selector, executed by the serial native-build owner, is:

```text
--saved-equipped-corpse <owned-game-root> <mod-id> <mod-root> <complete-checkpoint> <actor-plugin:hex-id> <attacker-plugin:hex-id> [dependencies...]
```

It extends `NativeActorPerformanceAudit`'s existing stopped-corpse fixture.
It uses the selected original actor's source equipment entry points, real source
body contacts/ragdoll and an actual resident door/capsule contact. A diagnostic
pending-query lease checks exact-once death retirement while the real source
door remains moving. Source shot audio, when declared, must reach actual native
Finished; a declared muzzle must finish through its existing playback owner.
Native source-graph corruption must refuse before weapon setters. Current/cold
capture and child-first retirement compare the complete item/condition/magazine/
random, equipment and stopped source-fault receipts. Missing actual items,
over-capacity magazines and a changed inventory/virtual-ammo policy must reject
atomically without changing the current native inventory or handling owner.
The audit checks unchanged
checkpoint input, starts no recording and cannot establish ordinary gameplay.

The pure selector and the unchanged owned melee guard's native selector pass
current/cold/child-first retirement, eighteen physical bodies, original route/
door/fault retention, two fixture attachments and exact inventory/handling.
The source-selected melee weapon has no projectile muzzle; ranged effect and
audio assertions apply only when actually declared and independently started.

Actual ordinary source-route combat defeats both guards and the real paused
Create New Save writes a complete v45 Atrium slot at Escape stage 35, 120 HP and
2/24 rounds. Ordinary quit/cold Continue verifies both dead guards and native
equipment, cell/stage/health/ammunition. Full publication gating, ordinary
equipped-item transfer, exterior/campaign saving, source drop semantics and
matched retail event timing/pixels remain independent requirements.

The focused transfer selector is:

```powershell
dotnet run --project .\contract-tests\ReferenceScriptContractProbe\ReferenceScriptContractProbe.csproj -- --corpse-equipped-loot-contracts
```

Pure transfer/source-command/current/cold tests pass ammo/condition/ownership,
selective retirement, late overflow, post-publication rollback, nested mutation
and moving/pending/opaque/partial-stack refusals.

The owned native selector is:

```text
--saved-equipped-corpse-loot <owned-game-root> <mod-id> <mod-root> <complete-v45-checkpoint> <actor-plugin:hex-id> [dependencies...]
```

It starts from an already-dead genuinely saved source actor. Actual default
activation opens the owned container and its real Take All uses the common
inventory owner. Current/cold/child-first retirement, source RemoveItem, genuine
finite audio, injected later-owner rollback, node/material order, pending/awake/
opaque negatives and living removal refusal pass the selected owned component.
Actual ordinary Take All from source 064913 transfers one police baton and one
source armor item with other counts, 120 HP and 2/24 rounds unchanged.
Paused Create New Save and ordinary quit/cold Continue verify the transferred
items, original corpse fault and empty attachment/handling state without replay.
The full required local gate and selected owned audit pass. Checked publication,
whole loot/other equipment and matched retail/drop behavior remain independent.
