"""Resolve effective typed actor records across a master-aware plugin stack."""

from __future__ import annotations

from dataclasses import dataclass

from actor_catalog import (
    ActorCatalog,
    AppearancePart,
    Armor,
    CreatureActor,
    HumanoidActor,
    LeveledList,
    RaceAppearance,
    scan_actor_catalog,
)
from plugin_stack import FormKey, PluginContext


HUMANOID_RECORD_TYPE = "NPC_"
CREATURE_RECORD_TYPE = "CREA"
RACE_RECORD_TYPE = "RACE"
ARMOR_RECORD_TYPE = "ARMO"
LEVELED_ITEM_RECORD_TYPE = "LVLI"
FORM_ID_RADIX = 16
APPEARANCE_PART_RECORD_TYPES = frozenset({"EYES", "HAIR", "HDPT"})
SOURCE_RECORD_TYPES = frozenset(
    {
        HUMANOID_RECORD_TYPE,
        CREATURE_RECORD_TYPE,
        RACE_RECORD_TYPE,
        ARMOR_RECORD_TYPE,
        LEVELED_ITEM_RECORD_TYPE,
        *APPEARANCE_PART_RECORD_TYPES,
    }
)


@dataclass(frozen=True)
class SourcedRecord:
    """One effective typed record and the plugin namespace that authored it."""

    key: FormKey
    record_type: str
    context: PluginContext
    value: object

    def linked_key(self, raw_form_id: int | None) -> FormKey | None:
        return self.context.form_key(raw_form_id or 0, optional=True)


@dataclass(frozen=True)
class ActorSourceStack:
    """Effective visual source records after official load-order merge."""

    contexts: tuple[PluginContext, ...]
    humanoids: dict[FormKey, SourcedRecord]
    creatures: dict[FormKey, SourcedRecord]
    races: dict[FormKey, SourcedRecord]
    parts: dict[FormKey, SourcedRecord]
    armor: dict[FormKey, SourcedRecord]
    leveled_items: dict[FormKey, SourcedRecord]

    def base(self, key: FormKey) -> SourcedRecord:
        matches = [
            source
            for source in (self.humanoids.get(key), self.creatures.get(key))
            if source is not None
        ]
        if len(matches) != 1:
            raise KeyError(f"Expected one effective actor base {key.text}, found {len(matches)}")
        return matches[0]


def parse_form_key(value: str) -> FormKey:
    owner, separator, object_text = value.rpartition(":")
    if not separator or not owner or not object_text:
        raise ValueError(f"Invalid stable FormKey: {value!r}")
    try:
        object_id = int(object_text, FORM_ID_RADIX)
    except ValueError as error:
        raise ValueError(f"Invalid stable FormKey: {value!r}") from error
    return FormKey(owner, object_id)


def _mapping(
    stack: ActorSourceStack,
    signature: str,
) -> dict[FormKey, SourcedRecord]:
    if signature == HUMANOID_RECORD_TYPE:
        return stack.humanoids
    if signature == CREATURE_RECORD_TYPE:
        return stack.creatures
    if signature == RACE_RECORD_TYPE:
        return stack.races
    if signature in APPEARANCE_PART_RECORD_TYPES:
        return stack.parts
    if signature == ARMOR_RECORD_TYPE:
        return stack.armor
    if signature == LEVELED_ITEM_RECORD_TYPE:
        return stack.leveled_items
    raise ValueError(f"Unsupported actor source record type: {signature}")


def _typed_records(
    catalog: ActorCatalog,
) -> tuple[tuple[str, dict[int, object]], ...]:
    return (
        (HUMANOID_RECORD_TYPE, catalog.actors),
        (CREATURE_RECORD_TYPE, catalog.creatures),
        (RACE_RECORD_TYPE, catalog.races),
        *( (signature, {
                form_id: part
                for form_id, part in catalog.parts.items()
                if part.record_type == signature
            })
            for signature in sorted(APPEARANCE_PART_RECORD_TYPES)
        ),
        (ARMOR_RECORD_TYPE, catalog.armor),
        (LEVELED_ITEM_RECORD_TYPE, catalog.leveled_lists),
    )


def _merge_catalog(
    stack: ActorSourceStack,
    context: PluginContext,
    catalog: ActorCatalog,
    record_types: dict[FormKey, str],
) -> None:
    for signature in sorted(SOURCE_RECORD_TYPES):
        target = _mapping(stack, signature)
        for raw_form_id in sorted(catalog.deleted_form_ids.get(signature, set())):
            key = context.form_key(raw_form_id)
            assert key is not None
            target.pop(key, None)
            record_types.pop(key, None)
    for signature, records in _typed_records(catalog):
        target = _mapping(stack, signature)
        for raw_form_id, value in records.items():
            key = context.form_key(raw_form_id)
            assert key is not None
            previous_type = record_types.get(key)
            if previous_type is not None and previous_type != signature:
                raise ValueError(
                    f"Form {key.text} changes source type from {previous_type} to {signature}"
                )
            target[key] = SourcedRecord(key, signature, context, value)
            record_types[key] = signature


def build_actor_source_stack(
    contexts: tuple[PluginContext, ...],
) -> ActorSourceStack:
    """Scan and merge the visual record types used by generic actor assembly."""

    stack = ActorSourceStack(contexts, {}, {}, {}, {}, {}, {})
    record_types: dict[FormKey, str] = {}
    for context in contexts:
        _merge_catalog(stack, context, scan_actor_catalog(context.path), record_types)
    return stack


def require_humanoid(source: SourcedRecord) -> HumanoidActor:
    if source.record_type != HUMANOID_RECORD_TYPE or not isinstance(source.value, HumanoidActor):
        raise TypeError(f"Expected NPC_ source, found {source.record_type}")
    return source.value


def require_creature(source: SourcedRecord) -> CreatureActor:
    if source.record_type != CREATURE_RECORD_TYPE or not isinstance(source.value, CreatureActor):
        raise TypeError(f"Expected CREA source, found {source.record_type}")
    return source.value


def require_race(source: SourcedRecord) -> RaceAppearance:
    if source.record_type != RACE_RECORD_TYPE or not isinstance(source.value, RaceAppearance):
        raise TypeError(f"Expected RACE source, found {source.record_type}")
    return source.value


def require_part(source: SourcedRecord) -> AppearancePart:
    if source.record_type not in APPEARANCE_PART_RECORD_TYPES or not isinstance(source.value, AppearancePart):
        raise TypeError(f"Expected appearance-part source, found {source.record_type}")
    return source.value


def require_armor(source: SourcedRecord) -> Armor:
    if source.record_type != ARMOR_RECORD_TYPE or not isinstance(source.value, Armor):
        raise TypeError(f"Expected ARMO source, found {source.record_type}")
    return source.value


def require_leveled_item(source: SourcedRecord) -> LeveledList:
    if source.record_type != LEVELED_ITEM_RECORD_TYPE or not isinstance(source.value, LeveledList):
        raise TypeError(f"Expected LVLI source, found {source.record_type}")
    return source.value
