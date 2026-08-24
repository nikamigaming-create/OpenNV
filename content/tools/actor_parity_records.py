"""Map typed actor catalogs into canonical load-order rows."""

from __future__ import annotations

import hashlib
import struct
from dataclasses import dataclass

from actor_catalog import (
    ACTOR_RECORD_TYPES,
    ActorCatalog,
    ActorItem,
    ActorReference,
    CreatureActor,
    HumanoidActor,
    LeveledActorList,
    scan_actor_catalog,
)
from plugin_stack import (
    FORM_ID_HEX_CHARACTERS,
    FormKey,
    PluginContext,
    form_link,
    runtime_form_id,
)


HUMANOID_RECORD_TYPE = "NPC_"
CREATURE_RECORD_TYPE = "CREA"
HUMANOID_REFERENCE_TYPE = "ACHR"
CREATURE_REFERENCE_TYPE = "ACRE"
LEVELED_HUMANOID_TYPE = "LVLN"
LEVELED_CREATURE_TYPE = "LVLC"
BASE_RECORD_TYPES = frozenset({HUMANOID_RECORD_TYPE, CREATURE_RECORD_TYPE})
REFERENCE_RECORD_TYPES = frozenset({HUMANOID_REFERENCE_TYPE, CREATURE_REFERENCE_TYPE})
LEVELED_ACTOR_RECORD_TYPES = frozenset({LEVELED_HUMANOID_TYPE, LEVELED_CREATURE_TYPE})
MERGED_RECORD_TYPES = BASE_RECORD_TYPES | REFERENCE_RECORD_TYPES | LEVELED_ACTOR_RECORD_TYPES


@dataclass
class MergeState:
    bases: dict[FormKey, dict[str, object]]
    leveled_lists: dict[FormKey, dict[str, object]]
    placements: dict[FormKey, dict[str, object]]
    raw_counts: dict[str, dict[str, int]]
    override_counts: dict[str, int]
    deletion_counts: dict[str, int]


def source_record(
    context: PluginContext,
    catalog: ActorCatalog,
    signature: str,
    raw_form_id: int,
    load_order_indices: dict[str, int],
) -> dict[str, object]:
    key = context.form_key(raw_form_id)
    assert key is not None
    return {
        "formKey": key.text,
        "runtimeFormId": runtime_form_id(key, load_order_indices),
        "recordType": signature,
        "sourcePlugin": context.name,
        "sourceLocalFormId": f"{raw_form_id:0{FORM_ID_HEX_CHARACTERS}x}",
        "recordFlags": f"{catalog.record_flags[signature][raw_form_id]:0{FORM_ID_HEX_CHARACTERS}x}",
        "recordDataSha256": catalog.record_data_sha256[signature][raw_form_id],
    }


def float_array_contract(values: tuple[float, ...]) -> dict[str, object]:
    if not values:
        return {"samples": 0, "sha256": None}
    payload = struct.pack(f"<{len(values)}f", *values)
    return {"samples": len(values), "sha256": hashlib.sha256(payload).hexdigest()}


def inventory_rows(
    context: PluginContext,
    inventory: tuple[ActorItem, ...],
    load_order_indices: dict[str, int],
) -> list[dict[str, object]]:
    return [
        {
            "item": form_link(context, entry.form_id, load_order_indices),
            "count": entry.count,
        }
        for entry in inventory
    ]


def humanoid_row(
    context: PluginContext,
    catalog: ActorCatalog,
    actor: HumanoidActor,
    load_order_indices: dict[str, int],
) -> dict[str, object]:
    row = source_record(
        context,
        catalog,
        HUMANOID_RECORD_TYPE,
        actor.form_id,
        load_order_indices,
    )
    row.update(
        {
            "editorId": actor.editor_id,
            "name": actor.name,
            "actorFlags": actor.actor_flags,
            "sex": "female" if actor.female else "male",
            "skeletonPath": actor.skeleton_path,
            "race": form_link(context, actor.race_form_id, load_order_indices),
            "hair": form_link(context, actor.hair_form_id, load_order_indices),
            "eyes": form_link(context, actor.eyes_form_id, load_order_indices),
            "headParts": [
                form_link(context, value, load_order_indices)
                for value in actor.head_part_form_ids
            ],
            "hairLength": actor.hair_length,
            "hairColorRgba": list(actor.hair_color_rgba),
            "inventory": inventory_rows(context, actor.inventory, load_order_indices),
            "template": form_link(context, actor.template_form_id, load_order_indices),
            "templateFlags": actor.template_flags,
            "faceGen": {
                "symmetricGeometry": float_array_contract(actor.face_symmetric_geometry),
                "asymmetricGeometry": float_array_contract(actor.face_asymmetric_geometry),
                "symmetricTexture": float_array_contract(actor.face_symmetric_texture),
            },
        }
    )
    return row


def creature_row(
    context: PluginContext,
    catalog: ActorCatalog,
    creature: CreatureActor,
    load_order_indices: dict[str, int],
) -> dict[str, object]:
    row = source_record(
        context,
        catalog,
        CREATURE_RECORD_TYPE,
        creature.form_id,
        load_order_indices,
    )
    row.update(
        {
            "editorId": creature.editor_id,
            "name": creature.name,
            "actorFlags": creature.actor_flags,
            "skeletonPath": creature.skeleton_path,
            "modelPaths": list(creature.model_paths),
            "inventory": inventory_rows(context, creature.inventory, load_order_indices),
            "template": form_link(context, creature.template_form_id, load_order_indices),
            "templateFlags": creature.template_flags,
        }
    )
    return row


def leveled_actor_row(
    context: PluginContext,
    catalog: ActorCatalog,
    leveled: LeveledActorList,
    load_order_indices: dict[str, int],
) -> dict[str, object]:
    row = source_record(
        context,
        catalog,
        leveled.record_type,
        leveled.form_id,
        load_order_indices,
    )
    row.update(
        {
            "editorId": leveled.editor_id,
            "entries": [
                {
                    "baseOrList": form_link(context, entry.form_id, load_order_indices),
                    "count": entry.count,
                }
                for entry in leveled.entries
            ],
        }
    )
    return row


def placement_row(
    context: PluginContext,
    catalog: ActorCatalog,
    reference: ActorReference,
    load_order_indices: dict[str, int],
) -> dict[str, object]:
    row = source_record(
        context,
        catalog,
        reference.record_type,
        reference.form_id,
        load_order_indices,
    )
    row.update(
        {
            "baseOrList": form_link(context, reference.actor_form_id, load_order_indices),
            "cell": form_link(context, reference.cell_form_id, load_order_indices),
            "initiallyDisabled": reference.initially_disabled,
            "positionGameUnits": list(reference.position),
            "rotationRadians": list(reference.rotation_radians),
            "scale": reference.scale,
        }
    )
    return row


def mapping_for_signature(
    state: MergeState,
    signature: str,
) -> dict[FormKey, dict[str, object]]:
    if signature in BASE_RECORD_TYPES:
        return state.bases
    if signature in LEVELED_ACTOR_RECORD_TYPES:
        return state.leveled_lists
    if signature in REFERENCE_RECORD_TYPES:
        return state.placements
    raise ValueError(f"Unsupported merged actor record type: {signature}")


def merge_row(
    state: MergeState,
    key: FormKey,
    row: dict[str, object],
) -> None:
    signature = str(row["recordType"])
    mapping = mapping_for_signature(state, signature)
    previous = mapping.get(key)
    if previous is not None:
        previous_signature = str(previous["recordType"])
        if previous_signature != signature:
            raise ValueError(
                f"Form {key.text} changes actor record type from "
                f"{previous_signature} to {signature}"
            )
        state.override_counts[signature] = state.override_counts.get(signature, 0) + 1
    mapping[key] = row


def apply_plugin(
    state: MergeState,
    context: PluginContext,
    load_order_indices: dict[str, int],
) -> None:
    catalog = scan_actor_catalog(context.path)
    state.raw_counts[context.name] = {
        signature: catalog.record_counts.get(signature, 0)
        for signature in sorted(ACTOR_RECORD_TYPES)
    }
    for signature in sorted(MERGED_RECORD_TYPES):
        mapping = mapping_for_signature(state, signature)
        for raw_form_id in sorted(catalog.deleted_form_ids.get(signature, set())):
            key = context.form_key(raw_form_id)
            assert key is not None
            mapping.pop(key, None)
            state.deletion_counts[signature] = state.deletion_counts.get(signature, 0) + 1
    for actor in catalog.actors.values():
        key = context.form_key(actor.form_id)
        assert key is not None
        merge_row(state, key, humanoid_row(context, catalog, actor, load_order_indices))
    for creature in catalog.creatures.values():
        key = context.form_key(creature.form_id)
        assert key is not None
        merge_row(state, key, creature_row(context, catalog, creature, load_order_indices))
    for leveled in catalog.actor_leveled_lists.values():
        key = context.form_key(leveled.form_id)
        assert key is not None
        merge_row(
            state,
            key,
            leveled_actor_row(context, catalog, leveled, load_order_indices),
        )
    for reference in catalog.references:
        key = context.form_key(reference.form_id)
        assert key is not None
        merge_row(
            state,
            key,
            placement_row(context, catalog, reference, load_order_indices),
        )
