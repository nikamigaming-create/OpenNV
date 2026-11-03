# Current work

## Owner and objective

The November 3 user direction is offline-first FO3/FNV implementation and tests
across the complete game systems. Root owns integration and publication.
Do not launch a campaign during this block. Recording stays off.
The full system checklist and acceptance boundaries are in implementation-plan.md.

The first later gameplay acceptance routes remain ordinary FNV New Game through
Goodsprings and departure, and standalone FO3 through Vault 101 to populated,
interactive Megaton. Neither route nor either complete campaign is accepted.
Flat and XR share authoritative gameplay and saves; physical XR remains open.

## Verified state

The integrated offline batch is published through PR352 from
codex/playable-opening-routes, based on main
adacd1a8f18f550e94083e58d4116b06d29d49aa.
The earlier palette decoder, recipe/quantity controls and actor retirement
repairs remain integrated. Their component checks passed; gameplay is unverified.

The offline repair batch fixes three concrete shared-owner defects:

- Expression arguments and values now admit complete digit-leading source IDs
  while preserving numeric literals, inactive evaluation and existing identifiers.
- Local declaration admission reads the exact compiled variable name despite
  trailing editor-text punctuation; missing slots and conflicting kinds refuse.
- Barter records actual weapon/ammunition transfer order, including ammunition
  carried by a loaded gun without an explicit ammunition trade row.

Focused script and reference contracts pass. Eight barter cases cover both
directions, implicit/explicit/split ammo, rollback and cold state. Owned FO3 and
FNV 10mm-pistol reload/trade checks pass with source magazine capacity, exact
ammunition conservation, instance identity, script-entry history and cold restore.

Complete selected-stack quest audits read 628,471 winning FNV records and
930,306 FO3 records. The FNV selection includes the installed diagnostic weapon
comparison plugin; it is not a pristine official-only selection. Source stack
identities are unchanged across the comparison, all raw program fields remain
accounted for, and both audits instantiate zero world references.

Unbound expression statements fall from 1,308 to 1,294 in FNV and 844 to 808 in
FO3. FO3 local-declaration failures fall from one to zero. FNV retains 12
source-parser and three compiled-extent findings; FO3 retains ten source-parser
and one compiled-extent finding. Empty-program stale metadata findings are
distinct from missing executable behavior. Statement dispatch remains uninspected
for 51,518 FNV and 28,988 FO3 statements. Neither audit establishes their effects.

Both broad readiness audits intentionally exit one: complete runtime readiness
is false. SCDA instruction execution, remaining source failures, all quest
branches, dialogue/combat/effects, housing CELL ownership and complete cold
continuation remain open. Passing selected tests is not whole-game acceptance.

## Next owner and publication

The complete required runtime gate passes: Release/Debug, formatting/analyzers,
contract probes, native plugin domains, launcher and headless Godot components.
Diff review and Release export pass. The local executable is
tmp/development-runtime/windows/OpenNV.exe; export did not launch a campaign.
Root owns checked PR352 publication, merge and local/origin main equality.
The latest user instruction makes offline verification the publication scope;
do not imply that merge accepts either opening route or whole-game parity.

Private current audit rows are under local/offline-capabilities-current-20261103.
Keep only the compact baseline comparison and the active current diagnostics,
not duplicate audit trees. No owned files, saves, binaries or derivatives belong
in the PR. No campaign has been launched during this block.

After checked publication, the next implementation owner is the compiled-script
and remaining source-admission gap, using the retained exact failures. FO3-specific
weapon damage/repair rules, broader effects and housing access remain visible
additional gaps. Actual ordinary-input opening-route and fresh-process save
continuation follow the offline block; matched retail evidence is additionally
required before parity claims.
