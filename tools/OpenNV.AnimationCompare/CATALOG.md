# Owned animation catalog

`catalog <installation-or-source-stack> <fresh-output-directory>` reads the
selected winning plugin/resource graph in place. Existing `--mod`, `--mod-stack`
and `--mod-order` options use the same source selector as DevelopmentLab.
Output must stay private and outside the owned content roots. It contains
source identities, hashes, names and animation declarations; it is not a
redistributable asset bundle or a gameplay implementation.

`catalog.json` starts as incomplete and is finalized only after every stage.
A discovery error leaves the resource denominator explicitly incomplete.
Per-record, per-block, rig and action failures remain individual rows.
The files are newline-delimited JSON except the manifest:

- `catalog-records.jsonl`: every registered winning record, including deleted
  winners, payload hash, model/animation declarations and read failures.
- `catalog-resources.jsonl`: all winning NIF/KF resources from every mounted
  archive and loose directory, plus declared missing resources. Includes roots,
  named hierarchy, sequence/channel/text-key declarations, embedded time
  controllers and manager membership. Material, UV, visibility, morph and
  particle controllers are in the same denominator as bone transforms.
- `catalog-rigs.jsonl`: source NPC/CREA template/appearance graphs and the
  engine first-person rig. Equivalent source forms and original model paths
  remain explicit variants. Unknown graphs are never merged.
- `catalog-cases.jsonl`: resource, embedded-controller, skeletal-pose, skin-binding and weapon
  perspective/action cases. No-controller models retain an unresolved
  procedural-animation classification; they are not presumed static.
- `catalog-reference-sets.jsonl`: complete authored candidate sets. Cases join
  these with `referenceSetId`; inline candidate arrays are deliberately empty.
  `sourceDisabled` describes the source flag, not current quest/runtime state.
  The engine player reference has no invented source cell. An absent entry
  marker requires the runner to use a real checkpoint and actual residency.
- `catalog-failures.jsonl`: every recorded failure, with source identity/lane.
- `catalog-failure-groups.jsonl`: repair domains and counts, with bounded
  examples; the complete instances remain in the failure file.
- `catalog-source-owner-sets.jsonl`: every record owning a shared model. A NIF
  controller is exercised once per source binding, not once per NPC skin.
  Each record also retains its own state/activation admission case.
- `catalog-rig-sets.jsonl`: normalized equivalent and unresolved rig domains.
  Cases use `equivalentRigSetId` and `relatedRigSetId`; inline arrays stay empty.
- `catalog-weapon-variants.jsonl`: every declared modification combination,
  perspective model and source modifier effect/value. Missing gameplay-effect
  support is an execution blocker and does not erase source geometry/actions.

Full skin-binding equivalence retains all non-material bytes, strings, root structure,
controller bindings, rest transforms, skin weights and topology. Only selected
texture/material value blocks may differ; their type/index still participate.
This is deliberately conservative. It does not establish material/pixel parity.
Skeletal-pose cases use the completely decoded skeleton and the exact skeletal
transform/accumulation channel subset. An unrelated outfit/inventory failure
does not erase that skeleton. Every skin graph still has its own binding case;
there is no skin-times-clip product. Attachment and non-transform channels stay
in their resource/controller/action lanes and cannot inherit whole-clip parity
from a successful bone comparison. If appearance resolution fails, the model
template group may independently recover a declared skeleton, while the
incomplete appearance remains explicitly blocked.

Every weapon retains its functional source modification combinations, first and
third person, equip/unequip, aim/iron sights, attack and reload requests. A gun
is not multiplied onto skeletons lacking its source attachment. Equivalent
weapon-action graphs can alias skin rigs only when the selected sequence and
every skeleton/weapon/body target binding agree. `actionBindingHash` identifies
that graph; `equivalentRigSetId` records the aliases and their exact equivalence
role. `relatedRigSetId` on an unbound row is only a domain join, never an
equivalence claim. Skin-binding deformation remains in separate skin cases.
Unresolved source domains stay explicit instead of manufacturing runnable
Cartesian cases.

Source namespace membership and source hash equality do not qualify native
action selection, current conditions, actor equipment, blend contribution or
runtime admission. Every case starts with `coverage: not-run` and an unbound
execution owner. The controlled runner must qualify that owner, produce fresh
independent evidence and restore its actual checkpoint. Catalog enumeration,
pose agreement, events/audio, skin/materials, final pixels and 4K frame timing
are independent lanes. Schema v2 uses the shared rig-control canonical ordered
plugin hash for `sourceStackHash`. The separate `catalogContentHash` covers the
selected plugins, settings and catalog NIF/KF bytes; neither silently claims
texture/audio byte coverage. An old v1 manifest's content hash must never be
passed as the rig-control source-stack hash.
