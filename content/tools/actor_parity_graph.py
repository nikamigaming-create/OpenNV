"""Resolve actor template variants and placed-reference candidates."""

from __future__ import annotations


BASE_TO_LEVELED_TYPE = {"NPC_": "LVLN", "CREA": "LVLC"}
REFERENCE_TO_BASE_TYPE = {"ACHR": "NPC_", "ACRE": "CREA"}
NPC_RECORD_TYPE = "NPC_"
NPC_USE_TEMPLATE_TRAITS_ACTOR_FLAG = 0x00000100
TEMPLATE_CATEGORY_FLAGS = {
    "traits": 0x0001,
    "stats": 0x0002,
    "factions": 0x0004,
    "actorEffects": 0x0008,
    "aiData": 0x0010,
    "aiPackages": 0x0020,
    "model": 0x0040,
    "baseData": 0x0080,
    "inventory": 0x0100,
    "script": 0x0200,
}


def link_key(link: object) -> str | None:
    if not isinstance(link, dict):
        return None
    value = link.get("key")
    return str(value) if value is not None else None


def expand_template_paths(
    target_key: str,
    bases_by_text: dict[str, dict[str, object]],
    lists_by_text: dict[str, dict[str, object]],
    stack: tuple[str, ...],
) -> tuple[set[tuple[str, ...]], list[dict[str, object]]]:
    if target_key in stack:
        return set(), [
            {
                "kind": "template-cycle",
                "targetFormKey": target_key,
                "chain": [*stack, target_key],
            }
        ]
    base = bases_by_text.get(target_key)
    if base is not None:
        template_key = link_key(base.get("template"))
        if template_key is None:
            return {(target_key,)}, []
        tails, gaps = expand_template_paths(
            template_key,
            bases_by_text,
            lists_by_text,
            (*stack, target_key),
        )
        return {(target_key, *tail) for tail in tails}, gaps
    leveled = lists_by_text.get(target_key)
    if leveled is None:
        return set(), [
            {
                "kind": "unresolved-template-target",
                "targetFormKey": target_key,
                "chain": list(stack),
            }
        ]
    paths: set[tuple[str, ...]] = set()
    gaps: list[dict[str, object]] = []
    entries = leveled["entries"]
    if not entries:
        return set(), [
            {
                "kind": "empty-template-list",
                "targetFormKey": target_key,
                "chain": list(stack),
            }
        ]
    for entry in entries:
        entry_key = link_key(entry.get("baseOrList"))
        if entry_key is None:
            gaps.append(
                {
                    "kind": "null-template-list-entry",
                    "targetFormKey": target_key,
                    "chain": list(stack),
                }
            )
            continue
        tails, entry_gaps = expand_template_paths(
            entry_key,
            bases_by_text,
            lists_by_text,
            (*stack, target_key),
        )
        paths.update((target_key, *tail) for tail in tails)
        gaps.extend(entry_gaps)
    return paths, gaps


def resolve_template_variants(
    bases_by_text: dict[str, dict[str, object]],
    lists_by_text: dict[str, dict[str, object]],
) -> list[dict[str, object]]:
    gaps: list[dict[str, object]] = []
    for key, base in bases_by_text.items():
        paths, base_gaps = expand_template_paths(
            key,
            bases_by_text,
            lists_by_text,
            (),
        )
        for gap in base_gaps:
            gap["baseFormKey"] = key
        expected_base_type = str(base["recordType"])
        expected_list_type = BASE_TO_LEVELED_TYPE[expected_base_type]
        for path in paths:
            for target_key in path:
                target_base = bases_by_text.get(target_key)
                target_list = lists_by_text.get(target_key)
                if (
                    target_base is not None
                    and target_base["recordType"] != expected_base_type
                ) or (
                    target_list is not None
                    and target_list["recordType"] != expected_list_type
                ):
                    base_gaps.append(
                        {
                            "kind": "template-type-mismatch",
                            "baseFormKey": key,
                            "targetFormKey": target_key,
                            "expectedBaseType": expected_base_type,
                            "expectedListType": expected_list_type,
                        }
                    )
        gaps.extend(base_gaps)
        selection_paths = sorted(paths)
        base["templateSelectionPaths"] = [list(path) for path in selection_paths]
        variants: dict[tuple[tuple[str, str], ...], list[tuple[str, ...]]] = {}
        for path in selection_paths:
            sources = tuple(
                (
                    category,
                    category_source(path, bases_by_text, category, flag),
                )
                for category, flag in TEMPLATE_CATEGORY_FLAGS.items()
            )
            variants.setdefault(sources, []).append(path)
        base["appearanceVariants"] = [
            {
                "categorySources": dict(sources),
                "selectionPaths": [list(path) for path in variant_paths],
            }
            for sources, variant_paths in sorted(variants.items())
        ]
        base["templateResolutionStatus"] = (
            "direct"
            if link_key(base.get("template")) is None
            else "resolved"
            if paths and not base_gaps
            else "unresolved"
        )
    return gaps


def category_source(
    selection_path: tuple[str, ...],
    bases_by_text: dict[str, dict[str, object]],
    category: str,
    category_flag: int,
) -> str:
    for target_key in selection_path:
        base = bases_by_text.get(target_key)
        if base is None:
            continue
        delegates = bool(int(base["templateFlags"]) & category_flag)
        if category == "traits" and base["recordType"] == NPC_RECORD_TYPE:
            delegates = delegates and bool(
                int(base["actorFlags"]) & NPC_USE_TEMPLATE_TRAITS_ACTOR_FLAG
            )
        if not delegates or link_key(base.get("template")) is None:
            return target_key
    raise ValueError(
        f"Template selection path has no terminal source for {category}: {selection_path}"
    )


def expand_base_candidates(
    target_key: str,
    bases_by_text: dict[str, dict[str, object]],
    lists_by_text: dict[str, dict[str, object]],
    stack: tuple[str, ...],
) -> tuple[set[str], list[dict[str, object]]]:
    if target_key in bases_by_text:
        return {target_key}, []
    leveled = lists_by_text.get(target_key)
    if leveled is None:
        return set(), [{"kind": "unresolved-base-or-list", "targetFormKey": target_key}]
    if target_key in stack:
        return set(), [
            {
                "kind": "leveled-list-cycle",
                "targetFormKey": target_key,
                "chain": [*stack, target_key],
            }
        ]
    candidates: set[str] = set()
    gaps: list[dict[str, object]] = []
    for entry in leveled["entries"]:
        entry_key = link_key(entry.get("baseOrList"))
        if entry_key is None:
            gaps.append(
                {
                    "kind": "null-leveled-entry",
                    "targetFormKey": target_key,
                }
            )
            continue
        entry_candidates, entry_gaps = expand_base_candidates(
            entry_key,
            bases_by_text,
            lists_by_text,
            (*stack, target_key),
        )
        candidates.update(entry_candidates)
        gaps.extend(entry_gaps)
    return candidates, gaps


def resolve_placement_candidates(
    placements_by_text: dict[str, dict[str, object]],
    bases_by_text: dict[str, dict[str, object]],
    lists_by_text: dict[str, dict[str, object]],
) -> list[dict[str, object]]:
    gaps: list[dict[str, object]] = []
    for key, placement in placements_by_text.items():
        target_key = link_key(placement.get("baseOrList"))
        if target_key is None:
            candidates: set[str] = set()
            placement_gaps = [{"kind": "null-placement-base", "placementFormKey": key}]
        else:
            candidates, placement_gaps = expand_base_candidates(
                target_key,
                bases_by_text,
                lists_by_text,
                (),
            )
        for gap in placement_gaps:
            gap["placementFormKey"] = key
        expected_base_type = REFERENCE_TO_BASE_TYPE[str(placement["recordType"])]
        for candidate in candidates:
            actual_base_type = str(bases_by_text[candidate]["recordType"])
            if actual_base_type != expected_base_type:
                placement_gaps.append(
                    {
                        "kind": "placement-type-mismatch",
                        "placementFormKey": key,
                        "candidateBaseFormKey": candidate,
                        "expectedBaseType": expected_base_type,
                        "actualBaseType": actual_base_type,
                    }
                )
        gaps.extend(placement_gaps)
        placement["candidateBaseFormKeys"] = sorted(candidates)
        placement["baseResolutionStatus"] = (
            "resolved" if candidates and not placement_gaps else "unresolved"
        )
    return gaps
