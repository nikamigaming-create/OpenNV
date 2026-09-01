"""Decode bounded, source-owned New Vegas crafting records."""

from __future__ import annotations

import re
import struct
from dataclasses import dataclass
from pathlib import Path

from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring


RECIPE_DATA = struct.Struct("<iiII")
FORM_ID = struct.Struct("<I")
QUANTITY = struct.Struct("<I")
RECIPE_CATEGORY_DATA_BYTES = 1
SHOW_RECIPE_MENU = re.compile(
    r"\b(?:player\s*\.\s*)?showrecipemenu\s+(?P<category>[A-Za-z_][A-Za-z0-9_]*)\b",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class CraftingIngredient:
    item_form_id: int
    count: int


@dataclass(frozen=True)
class CraftingRecipe:
    form_id: int
    editor_id: str
    display_name: str
    skill_actor_value_form_id: int | None
    required_skill_level: int
    category_form_id: int
    subcategory_form_id: int
    ingredients: tuple[CraftingIngredient, ...]
    outputs: tuple[CraftingIngredient, ...]
    condition_data: tuple[bytes, ...]


@dataclass(frozen=True)
class CraftingCategory:
    form_id: int
    editor_id: str
    display_name: str
    source_kind: int


@dataclass(frozen=True)
class CraftingCatalog:
    categories: dict[int, CraftingCategory]
    recipes: dict[int, CraftingRecipe]

    @classmethod
    def from_plugin(cls, path: Path) -> "CraftingCatalog":
        categories: dict[int, CraftingCategory] = {}
        recipes: dict[int, CraftingRecipe] = {}
        for record in iter_plugin_records(path, frozenset({"RCCT", "RCPE"})):
            if record.signature == "RCCT":
                category = decode_category(record)
                categories[category.form_id] = category
            else:
                recipe = decode_recipe(record)
                recipes[recipe.form_id] = recipe
        return cls(categories, recipes)

    def recipes_for_category(self, category_form_id: int) -> tuple[CraftingRecipe, ...]:
        if category_form_id not in self.categories:
            raise ValueError(f"Crafting category does not resolve: {category_form_id:08x}")
        return tuple(
            sorted(
                (
                    recipe
                    for recipe in self.recipes.values()
                    if recipe.category_form_id == category_form_id
                ),
                key=lambda recipe: recipe.form_id,
            )
        )


def _identity(record: Record) -> tuple[str, str]:
    editor_id = ""
    display_name = ""
    for subrecord in iter_subrecords(record):
        if subrecord.signature == "EDID":
            editor_id = zstring(subrecord.data)
        elif subrecord.signature == "FULL":
            display_name = zstring(subrecord.data)
    if not editor_id or not display_name:
        raise ValueError(f"{record.signature} {record.form_id:08x} has incomplete identity")
    return editor_id, display_name


def decode_category(record: Record) -> CraftingCategory:
    if record.signature != "RCCT":
        raise ValueError(f"Expected RCCT, found {record.signature}")
    editor_id, display_name = _identity(record)
    data_rows = [
        row.data for row in iter_subrecords(record) if row.signature == "DATA"
    ]
    if len(data_rows) != 1 or len(data_rows[0]) != RECIPE_CATEGORY_DATA_BYTES:
        raise ValueError(f"RCCT {record.form_id:08x} has unsupported DATA layout")
    return CraftingCategory(
        record.form_id,
        editor_id,
        display_name,
        data_rows[0][0],
    )


def decode_recipe(record: Record) -> CraftingRecipe:
    if record.signature != "RCPE":
        raise ValueError(f"Expected RCPE, found {record.signature}")
    editor_id, display_name = _identity(record)
    rows = tuple(iter_subrecords(record))
    data_rows = [row.data for row in rows if row.signature == "DATA"]
    if len(data_rows) != 1 or len(data_rows[0]) != RECIPE_DATA.size:
        raise ValueError(f"RCPE {record.form_id:08x} has unsupported DATA layout")
    skill, required_level, category, subcategory = RECIPE_DATA.unpack(data_rows[0])
    if required_level < 0:
        raise ValueError(f"RCPE {record.form_id:08x} has a negative skill requirement")

    ingredients: list[CraftingIngredient] = []
    outputs: list[CraftingIngredient] = []
    pending: tuple[str, int] | None = None
    conditions: list[bytes] = []
    for row in rows:
        if row.signature in {"RCIL", "RCOD"}:
            if pending is not None or len(row.data) != FORM_ID.size:
                raise ValueError(f"RCPE {record.form_id:08x} has malformed item ordering")
            pending = (row.signature, FORM_ID.unpack(row.data)[0])
        elif row.signature == "RCQY":
            if pending is None or len(row.data) != QUANTITY.size:
                raise ValueError(f"RCPE {record.form_id:08x} has an orphan quantity")
            count = QUANTITY.unpack(row.data)[0]
            if count == 0:
                raise ValueError(f"RCPE {record.form_id:08x} has a zero quantity")
            target = ingredients if pending[0] == "RCIL" else outputs
            target.append(CraftingIngredient(pending[1], count))
            pending = None
        elif row.signature == "CTDA":
            conditions.append(row.data)
    if pending is not None or not ingredients or not outputs:
        raise ValueError(f"RCPE {record.form_id:08x} has incomplete inputs or outputs")
    return CraftingRecipe(
        record.form_id,
        editor_id,
        display_name,
        None if skill == -1 else skill,
        required_level,
        category,
        subcategory,
        tuple(ingredients),
        tuple(outputs),
        tuple(conditions),
    )


def recipe_menu_category_editor_id(script_source: str) -> str:
    matches = {match.group("category") for match in SHOW_RECIPE_MENU.finditer(script_source)}
    if len(matches) != 1:
        raise ValueError("Crafting station script must select exactly one recipe category")
    return matches.pop()
