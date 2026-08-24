"""Resolve TES4 plugin-local FormIDs into stable load-order identities."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path

from plugin_records import read_plugin_masters


FORM_ID_OBJECT_BITS = 24
FORM_ID_NAMESPACE_BITS = 8
FORM_ID_NAMESPACE_COUNT = 1 << FORM_ID_NAMESPACE_BITS
FORM_ID_OBJECT_MASK = (1 << FORM_ID_OBJECT_BITS) - 1
FORM_ID_OBJECT_HEX_CHARACTERS = 6
FORM_ID_HEX_CHARACTERS = 8
HASH_READ_BYTES = 1024 * 1024


@dataclass(frozen=True, order=True)
class FormKey:
    owner_plugin: str
    object_id: int

    @property
    def text(self) -> str:
        return (
            f"{self.owner_plugin}:"
            f"{self.object_id:0{FORM_ID_OBJECT_HEX_CHARACTERS}x}"
        )


@dataclass(frozen=True)
class PluginContext:
    name: str
    path: Path
    load_order_index: int
    masters: tuple[str, ...]
    namespaces: tuple[str, ...]
    sha256: str
    bytes: int

    def form_key(self, raw_form_id: int, *, optional: bool = False) -> FormKey | None:
        if optional and raw_form_id == 0:
            return None
        local_index = raw_form_id >> FORM_ID_OBJECT_BITS
        if local_index >= len(self.namespaces):
            raise ValueError(
                f"{self.name} form {raw_form_id:08x} uses undeclared local index "
                f"{local_index}; namespaces={self.namespaces}"
            )
        return FormKey(
            self.namespaces[local_index],
            raw_form_id & FORM_ID_OBJECT_MASK,
        )


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(HASH_READ_BYTES):
            digest.update(chunk)
    return digest.hexdigest()


def find_case_insensitive_file(root: Path, expected_name: str) -> Path:
    matches = [
        path
        for path in root.iterdir()
        if path.is_file() and path.name.casefold() == expected_name.casefold()
    ]
    if len(matches) != 1:
        raise FileNotFoundError(
            f"Expected exactly one {expected_name!r} in {root}, found {len(matches)}"
        )
    return matches[0]


def build_plugin_stack(
    data_root: Path,
    configured_names: list[str],
) -> tuple[PluginContext, ...]:
    if len(configured_names) > FORM_ID_NAMESPACE_COUNT:
        raise ValueError("Plugin stack exceeds the TES4 FormID namespace")
    configured_by_fold = {name.casefold(): name for name in configured_names}
    if len(configured_by_fold) != len(configured_names):
        raise ValueError("Plugin stack contains duplicate names")
    contexts: list[PluginContext] = []
    loaded_names: set[str] = set()
    for load_order_index, configured_name in enumerate(configured_names):
        path = find_case_insensitive_file(data_root, configured_name)
        declared_masters = read_plugin_masters(path)
        canonical_masters: list[str] = []
        for declared_master in declared_masters:
            canonical = configured_by_fold.get(declared_master.casefold())
            if canonical is None:
                raise ValueError(
                    f"{configured_name} requires master outside the stack: {declared_master}"
                )
            if canonical.casefold() not in loaded_names:
                raise ValueError(
                    f"{configured_name} master is not earlier in load order: {canonical}"
                )
            canonical_masters.append(canonical)
        contexts.append(
            PluginContext(
                configured_name,
                path,
                load_order_index,
                tuple(canonical_masters),
                (*canonical_masters, configured_name),
                file_sha256(path),
                path.stat().st_size,
            )
        )
        loaded_names.add(configured_name.casefold())
    return tuple(contexts)


def load_order_indices(contexts: tuple[PluginContext, ...]) -> dict[str, int]:
    return {context.name.casefold(): context.load_order_index for context in contexts}


def runtime_form_id(key: FormKey, indices: dict[str, int]) -> str:
    load_index = indices[key.owner_plugin.casefold()]
    value = (load_index << FORM_ID_OBJECT_BITS) | key.object_id
    return f"{value:0{FORM_ID_HEX_CHARACTERS}x}"


def form_link(
    context: PluginContext,
    raw_form_id: int | None,
    indices: dict[str, int],
) -> dict[str, str] | None:
    if raw_form_id is None:
        return None
    key = context.form_key(raw_form_id, optional=True)
    if key is None:
        return None
    return {
        "key": key.text,
        "runtimeFormId": runtime_form_id(key, indices),
    }
