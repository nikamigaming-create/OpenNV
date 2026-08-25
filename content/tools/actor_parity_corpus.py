#!/usr/bin/env python3
"""Build the effective whole-game actor and creature parity review corpus."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

from actor_parity_graph import resolve_placement_candidates, resolve_template_variants
from actor_parity_records import (
    CREATURE_RECORD_TYPE,
    CREATURE_REFERENCE_TYPE,
    HUMANOID_RECORD_TYPE,
    HUMANOID_REFERENCE_TYPE,
    MergeState,
    apply_plugin,
)
from corpus_io import atomic_bytes, atomic_json, jsonl_bytes, output_descriptor
from plugin_stack import (
    build_plugin_stack,
    load_order_indices as plugin_load_order_indices,
)


RECIPE_SCHEMA = "opennv-actor-parity-corpus-recipe/v1"
CORPUS_SCHEMA = "opennv-actor-parity-corpus/v1"
VARIANT_ID_HEX_CHARACTERS = 16
OUTPUT_FILE_NAMES = {
    "bases": "actor-bases.jsonl",
    "leveledLists": "actor-leveled-lists.jsonl",
    "placements": "actor-placements.jsonl",
    "appearanceReview": "appearance-review.jsonl",
    "placementReview": "placement-review.jsonl",
    "gaps": "relationship-gaps.jsonl",
}
MANIFEST_FILE_NAME = "manifest.json"
EXIT_DATA_ERROR = 2


def load_recipe(recipe_path: Path) -> dict[str, object]:
    document = json.loads(recipe_path.read_text(encoding="utf-8"))
    if document.get("schema") != RECIPE_SCHEMA:
        raise ValueError(f"Unexpected actor parity corpus recipe schema: {recipe_path}")
    plugins = document.get("plugins")
    if not isinstance(plugins, list) or not plugins:
        raise ValueError("Actor parity corpus recipe must declare a non-empty plugin order")
    names = [str(row["file"]) for row in plugins]
    if len({name.casefold() for name in names}) != len(names):
        raise ValueError("Actor parity corpus recipe contains duplicate plugin names")
    return document


def appearance_review_rows(
    bases: list[dict[str, object]],
    bases_by_text: dict[str, dict[str, object]],
    humanoid_shots: list[object],
    creature_shots: list[object],
) -> list[dict[str, object]]:
    reviews = []
    for row in bases:
        for variant in row["appearanceVariants"]:
            appearance_signature = hashlib.sha256(
                json.dumps(
                    variant["categorySources"],
                    sort_keys=True,
                    separators=(",", ":"),
                ).encode("utf-8")
            ).hexdigest()
            variant_id = appearance_signature[:VARIANT_ID_HEX_CHARACTERS]
            reviews.append(
                {
                    "reviewKey": f"{row['formKey']}@{variant_id}",
                    "baseFormKey": row["formKey"],
                    "baseRuntimeFormId": row["runtimeFormId"],
                    "recordType": row["recordType"],
                    "editorId": row["editorId"],
                    "appearanceSignatureSha256": appearance_signature,
                    "categorySources": variant["categorySources"],
                    "categorySourceRuntimeFormIds": {
                        category: bases_by_text[source]["runtimeFormId"]
                        for category, source in variant["categorySources"].items()
                    },
                    "templateSelectionPaths": variant["selectionPaths"],
                    "requiredShots": humanoid_shots
                    if row["recordType"] == HUMANOID_RECORD_TYPE
                    else creature_shots,
                    "retailEvidenceStatus": "pending",
                    "godotEvidenceStatus": "pending",
                    "matchedComparisonStatus": "pending",
                }
            )
    return reviews


def placement_review_rows(
    placements: list[dict[str, object]],
    bases_by_text: dict[str, dict[str, object]],
    placement_shots: list[object],
) -> list[dict[str, object]]:
    return [
        {
            "placementFormKey": row["formKey"],
            "placementRuntimeFormId": row["runtimeFormId"],
            "recordType": row["recordType"],
            "cell": row["cell"],
            "candidateBaseFormKeys": row["candidateBaseFormKeys"],
            "candidateBaseRuntimeFormIds": [
                bases_by_text[key]["runtimeFormId"]
                for key in row["candidateBaseFormKeys"]
            ],
            "requiredShots": placement_shots,
            "retailEvidenceStatus": "pending",
            "godotEvidenceStatus": "pending",
            "matchedComparisonStatus": "pending",
        }
        for row in placements
    ]


def build_corpus(
    data_root: Path,
    output_root: Path,
    recipe: dict[str, object],
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite actor parity corpus: {output_root}")
    configured_names = [str(row["file"]) for row in recipe["plugins"]]
    contexts = build_plugin_stack(data_root, configured_names)
    load_order_indices = plugin_load_order_indices(contexts)
    state = MergeState({}, {}, {}, {}, {}, {})
    for context in contexts:
        apply_plugin(state, context, load_order_indices)

    bases = sorted(state.bases.values(), key=lambda row: str(row["formKey"]))
    leveled_lists = sorted(
        state.leveled_lists.values(),
        key=lambda row: str(row["formKey"]),
    )
    placements = sorted(
        state.placements.values(),
        key=lambda row: str(row["formKey"]),
    )
    bases_by_text = {str(row["formKey"]): row for row in bases}
    lists_by_text = {str(row["formKey"]): row for row in leveled_lists}
    placements_by_text = {str(row["formKey"]): row for row in placements}
    gaps = resolve_template_variants(bases_by_text, lists_by_text)
    gaps.extend(
        resolve_placement_candidates(placements_by_text, bases_by_text, lists_by_text)
    )

    capture = recipe["capture"]
    appearance_review = appearance_review_rows(
        bases,
        bases_by_text,
        list(capture["humanoidAppearanceShots"]),
        list(capture["creatureAppearanceShots"]),
    )
    placement_review = placement_review_rows(
        placements,
        bases_by_text,
        list(capture["placementShots"]),
    )
    output_root.mkdir(parents=True)
    output_rows = {
        "bases": bases,
        "leveledLists": leveled_lists,
        "placements": placements,
        "appearanceReview": appearance_review,
        "placementReview": placement_review,
        "gaps": sorted(gaps, key=lambda row: json.dumps(row, sort_keys=True)),
    }
    descriptors: dict[str, dict[str, object]] = {}
    for name, rows in output_rows.items():
        path = output_root / OUTPUT_FILE_NAMES[name]
        atomic_bytes(path, jsonl_bytes(rows))
        descriptors[name] = output_descriptor(path, len(rows))

    humanoid_count = sum(
        row["recordType"] == HUMANOID_RECORD_TYPE for row in bases
    )
    creature_count = sum(
        row["recordType"] == CREATURE_RECORD_TYPE for row in bases
    )
    humanoid_reference_count = sum(
        row["recordType"] == HUMANOID_REFERENCE_TYPE for row in placements
    )
    creature_reference_count = sum(
        row["recordType"] == CREATURE_REFERENCE_TYPE for row in placements
    )
    appearance_shot_count = sum(
        len(row["requiredShots"]) for row in appearance_review
    )
    placement_shot_count = sum(
        len(row["requiredShots"]) for row in placement_review
    )
    unresolved_placements = sum(
        row["baseResolutionStatus"] != "resolved" for row in placements
    )
    unresolved_templates = sum(
        row["templateResolutionStatus"] in {"unresolved", "cycle"} for row in bases
    )
    humanoid_appearance_variants = sum(
        row["recordType"] == HUMANOID_RECORD_TYPE for row in appearance_review
    )
    creature_appearance_variants = sum(
        row["recordType"] == CREATURE_RECORD_TYPE for row in appearance_review
    )
    dynamic_appearance_bases = sum(
        len(row["appearanceVariants"]) > 1 for row in bases
    )
    maximum_appearance_variants = max(
        (len(row["appearanceVariants"]) for row in bases),
        default=0,
    )
    status = (
        "inventory-complete-review-pending"
        if not gaps
        else "inventory-built-with-relationship-gaps"
    )
    manifest = {
        "schema": CORPUS_SCHEMA,
        "recipeId": recipe["id"],
        "status": status,
        "scope": {
            "officialPluginsOnly": True,
            "modsIncluded": False,
            "everyEffectiveBaseScheduled": (
                {str(row["baseFormKey"]) for row in appearance_review}
                == {str(row["formKey"]) for row in bases}
            ),
            "everyEffectivePlacementScheduled": len(placement_review) == len(placements),
        },
        "inputs": [
            {
                "file": context.name,
                "loadOrderIndex": context.load_order_index,
                "masters": list(context.masters),
                "bytes": context.bytes,
                "sha256": context.sha256,
                "rawRecordCounts": state.raw_counts[context.name],
            }
            for context in contexts
        ],
        "effectiveCounts": {
            "humanoidBases": humanoid_count,
            "creatureBases": creature_count,
            "allBases": len(bases),
            "humanoidPlacements": humanoid_reference_count,
            "creaturePlacements": creature_reference_count,
            "allPlacements": len(placements),
            "leveledActorLists": len(leveled_lists),
            "appearanceReviewRows": len(appearance_review),
            "humanoidAppearanceVariants": humanoid_appearance_variants,
            "creatureAppearanceVariants": creature_appearance_variants,
            "dynamicAppearanceBases": dynamic_appearance_bases,
            "maximumAppearanceVariantsPerBase": maximum_appearance_variants,
            "placementReviewRows": len(placement_review),
            "requiredAppearanceShots": appearance_shot_count,
            "requiredPlacementShots": placement_shot_count,
            "relationshipGaps": len(gaps),
            "unresolvedTemplates": unresolved_templates,
            "unresolvedPlacements": unresolved_placements,
        },
        "loadOrderMerge": {
            "overridesApplied": dict(sorted(state.override_counts.items())),
            "deletionsApplied": dict(sorted(state.deletion_counts.items())),
        },
        "evidencePolicy": {
            "matchedRetailAndGodotStateRequired": True,
            "appearanceReviewStatusesStartPending": True,
            "placementReviewStatusesStartPending": True,
            "noParityClaimFromInventoryAlone": True,
        },
        "outputs": descriptors,
    }
    atomic_json(output_root / MANIFEST_FILE_NAME, manifest)
    return manifest


def default_recipe_path() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes" / "fnv-official-actor-parity-corpus-v1.json"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=default_recipe_path())
    args = parser.parse_args()
    try:
        recipe = load_recipe(args.recipe.resolve())
        manifest = build_corpus(
            args.data_root.resolve(),
            args.output_root.resolve(),
            recipe,
        )
    except Exception as error:
        print(f"OPENNV_ACTOR_PARITY_CORPUS_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_ACTOR_PARITY_CORPUS "
        + json.dumps(
            {
                "manifest": str((args.output_root / MANIFEST_FILE_NAME).resolve()),
                "status": manifest["status"],
                "effectiveCounts": manifest["effectiveCounts"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
