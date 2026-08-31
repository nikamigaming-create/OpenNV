#!/usr/bin/env python3
"""Resolve a validated TTW stack for owned profile compilers.

The adapter is deliberately read-only.  It resolves records by stable origin
FormKey and resources through an explicit archive order plus the registered
low-to-high data-root order.  It does not execute scripts or publish a world.
"""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

from bsa_archive import BsaArchive, ExtractedMember, canonical_member_path
from cell_catalog import cell_parent_form_id
from plugin_records import (
    Record,
    iter_plugin_records,
    iter_subrecords,
    read_plugin_masters,
    zstring,
)
from plugin_stack import (
    FORM_ID_OBJECT_MASK,
    PluginContext,
    file_sha256,
    parse_form_key,
    runtime_form_id,
)
from ttw_profile import (
    DEFAULT_REQUIREMENTS_PATH,
    SCHEMA as TTW_PROFILE_SCHEMA,
    load_requirements,
    plugin_stack_id,
    read_active_load_order,
)
from ttw_source_namespace import (
    RESOLUTION_POLICY as SOURCE_NAMESPACE_RESOLUTION_POLICY,
    SCHEMA as SOURCE_NAMESPACE_SCHEMA,
    STATUS as SOURCE_NAMESPACE_STATUS,
)


PROFILE_STATUS = "validated-generated-plugin-profile"
DELETED_RECORD_FLAG = 0x00000020
SHA256_HEX_CHARACTERS = 64
COMPILER_SOURCE_SCHEMA = "opennv-ttw-effective-profile-compiler-source/v1"
EFFECTIVE_SOURCE_ORDER_SCHEMA = "opennv-ttw-effective-source-order/v1"
RECORD_RESOLUTION_POLICY = "stable-origin-formkey-last-active-plugin-wins"
SOURCE_ROOT_ORDER_POLICY = "registered-profile-source-roots-low-to-high"
LOOSE_MEMBER_PRECEDENCE = (
    "recursive-case-insensitive-last-source-root-wins-over-archives"
)
ARCHIVE_MEMBER_PRECEDENCE = (
    "first-containing-archive-wins-unless-later-archive-has-same-stem-zero-byte-override-marker"
)
CACHE_NAMESPACE = "ttw-effective-source"
CACHE_ID_PREFIX = b"opennv-ttw-effective-profile-compiler-source-v1\0"


@dataclass(frozen=True)
class RecordVersion:
    context: PluginContext
    source_root_index: int
    record: Record


@dataclass(frozen=True)
class TtwArchiveSource:
    name: str
    path: Path
    source_root_index: int
    sha256: str
    bytes: int
    archive: BsaArchive


@dataclass(frozen=True)
class ResolvedTtwMember:
    logical_path: str
    data: bytes
    winner: dict[str, object]
    overridden_versions: tuple[dict[str, object], ...]

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.data).hexdigest()

    def contract(self) -> dict[str, object]:
        return {
            "logicalPath": self.logical_path,
            "bytes": len(self.data),
            "sha256": self.sha256,
            "winner": self.winner,
            "overriddenVersions": list(self.overridden_versions),
        }


@dataclass(frozen=True)
class TtwResourceOrder:
    recipe_path: Path
    recipe_id: str
    archive_order: tuple[str, ...]
    override_markers: tuple[str, ...]

    @property
    def override_archives(self) -> frozenset[str]:
        return frozenset(
            f"{Path(marker).stem}.bsa".casefold() for marker in self.override_markers
        )

    def contract(self) -> dict[str, object]:
        return {
            "schema": EFFECTIVE_SOURCE_ORDER_SCHEMA,
            "recipe": {
                "id": self.recipe_id,
                "sha256": file_sha256(self.recipe_path),
            },
            "sourceRootOrder": SOURCE_ROOT_ORDER_POLICY,
            "looseMemberPrecedence": LOOSE_MEMBER_PRECEDENCE,
            "archiveMemberPrecedence": ARCHIVE_MEMBER_PRECEDENCE,
            "archiveOrder": list(self.archive_order),
            "overrideMarkers": list(self.override_markers),
        }


def _is_sha256(value: object) -> bool:
    return (
        isinstance(value, str)
        and len(value) == SHA256_HEX_CHARACTERS
        and all(character in "0123456789abcdef" for character in value)
    )


def validated_ttw_stack(
    profile_path: Path,
) -> tuple[
    dict[str, object],
    tuple[Path, ...],
    tuple[PluginContext, ...],
    dict[str, int],
]:
    """Revalidate one profile and return compiler-ready plugin contexts."""

    resolved_profile = profile_path.resolve()
    profile = json.loads(resolved_profile.read_text(encoding="utf-8"))
    if (
        profile.get("schema") != TTW_PROFILE_SCHEMA
        or profile.get("status") != PROFILE_STATUS
        or profile.get("kind") != "ttw"
    ):
        raise ValueError(f"Not a validated TTW profile: {resolved_profile}")

    root_rows = profile.get("sourceRoots")
    if not isinstance(root_rows, list) or not root_rows or not all(
        isinstance(row, str) for row in root_rows
    ):
        raise ValueError("TTW profile has no source roots")
    roots = tuple(Path(row).resolve() for row in root_rows)
    if len({str(root).casefold() for root in roots}) != len(roots):
        raise ValueError("TTW profile contains duplicate source roots")
    for root in roots:
        if not root.is_dir():
            raise FileNotFoundError(f"TTW profile source root is missing: {root}")

    source = profile.get("loadOrderSource")
    if (
        not isinstance(source, dict)
        or not isinstance(source.get("file"), str)
        or not _is_sha256(source.get("sha256"))
    ):
        raise ValueError("TTW profile load-order source is invalid")
    load_order_path = Path(str(source["file"])).resolve()
    if (
        not load_order_path.is_file()
        or file_sha256(load_order_path) != source["sha256"]
    ):
        raise ValueError("TTW profile load-order source changed")
    load_order = read_active_load_order(load_order_path)

    rows = profile.get("plugins")
    if not isinstance(rows, list) or not rows or not all(
        isinstance(row, dict) for row in rows
    ):
        raise ValueError("TTW profile contains no plugin rows")
    if tuple(str(row.get("file", "")) for row in rows) != load_order:
        raise ValueError("TTW profile plugin rows differ from its load-order source")
    configured_names = {
        str(row["file"]).casefold(): str(row["file"]) for row in rows
    }
    if len(configured_names) != len(rows):
        raise ValueError("TTW profile contains duplicate plugin rows")
    missing_markers = [
        name
        for name in load_requirements()
        if name.casefold() not in configured_names
    ]
    if missing_markers:
        raise ValueError(
            "TTW profile is missing required generated plugins: "
            + ", ".join(missing_markers)
        )

    contexts: list[PluginContext] = []
    validated_rows: list[dict[str, object]] = []
    for expected_index, row in enumerate(rows):
        name = str(row["file"])
        root_index = row.get("sourceRootIndex")
        if (
            not isinstance(root_index, int)
            or isinstance(root_index, bool)
            or not 0 <= root_index < len(roots)
        ):
            raise ValueError(f"TTW plugin has an invalid source root: {name}")
        if row.get("loadOrderIndex") != expected_index:
            raise ValueError(f"TTW plugin load-order index changed: {name}")
        winners: list[tuple[int, Path]] = []
        for index, root in enumerate(roots):
            matches = [
                path
                for path in root.iterdir()
                if path.is_file() and path.name.casefold() == name.casefold()
            ]
            if len(matches) > 1:
                raise ValueError(
                    f"TTW root contains duplicate case-insensitive plugin files: {name}"
                )
            if matches:
                winners.append((index, matches[0]))
        if not winners or winners[-1][0] != root_index:
            raise ValueError(f"TTW plugin effective source changed: {name}")
        path = winners[-1][1]
        if (
            path.stat().st_size != row.get("bytes")
            or file_sha256(path) != row.get("sha256")
        ):
            raise ValueError(f"TTW plugin bytes or hash changed: {name}")
        declared_masters = read_plugin_masters(path)
        masters = tuple(
            configured_names.get(master.casefold(), "") for master in declared_masters
        )
        recorded_masters = row.get("masters")
        if (
            any(not master for master in masters)
            or not isinstance(recorded_masters, list)
            or [master.casefold() for master in masters]
            != [str(master).casefold() for master in recorded_masters]
        ):
            raise ValueError(f"TTW plugin master list changed: {name}")
        if any(load_order.index(master) >= expected_index for master in masters):
            raise ValueError(f"TTW plugin master order changed: {name}")
        context = PluginContext(
            name=name,
            path=path,
            load_order_index=expected_index,
            masters=masters,
            namespaces=(*masters, name),
            sha256=str(row["sha256"]),
            bytes=int(row["bytes"]),
        )
        contexts.append(context)
        validated_rows.append(
            {
                "file": name,
                "loadOrderIndex": expected_index,
                "sourceRootIndex": root_index,
                "bytes": context.bytes,
                "sha256": context.sha256,
                "masters": list(recorded_masters),
            }
        )
    expected_stack_id = plugin_stack_id(validated_rows)
    if profile.get("pluginStackId") != expected_stack_id:
        raise ValueError("TTW plugin-stack identity changed")
    if profile.get("saveCompatibilityId") != f"ttw:{expected_stack_id}":
        raise ValueError("TTW save-compatibility identity changed")
    return (
        profile,
        roots,
        tuple(contexts),
        {name.casefold(): index for index, name in enumerate(load_order)},
    )


def validate_ttw_source_namespace(
    namespace_path: Path,
    profile_path: Path,
    profile: dict[str, object],
) -> dict[str, object]:
    resolved_namespace = namespace_path.resolve()
    namespace = json.loads(resolved_namespace.read_text(encoding="utf-8"))
    if (
        namespace.get("schema") != SOURCE_NAMESPACE_SCHEMA
        or namespace.get("status") != SOURCE_NAMESPACE_STATUS
        or namespace.get("resolutionPolicy") != SOURCE_NAMESPACE_RESOLUTION_POLICY
    ):
        raise ValueError(
            f"Not a validated TTW effective-source namespace: {resolved_namespace}"
        )
    source = namespace.get("sourceProfile")
    if not isinstance(source, dict):
        raise ValueError("TTW effective-source namespace has no source-profile binding")
    resolved_profile = profile_path.resolve()
    if (
        not isinstance(source.get("file"), str)
        or Path(str(source["file"])).resolve() != resolved_profile
        or source.get("sha256") != file_sha256(resolved_profile)
        or source.get("pluginStackId") != profile.get("pluginStackId")
        or source.get("saveCompatibilityId") != profile.get("saveCompatibilityId")
    ):
        raise ValueError("TTW effective-source namespace profile binding differs")
    expected_roots = [str(Path(row).resolve()) for row in profile["sourceRoots"]]
    if namespace.get("sourceRoots") != expected_roots:
        raise ValueError("TTW effective-source namespace roots differ from its profile")
    if namespace.get("plugins") != profile.get("plugins"):
        raise ValueError("TTW effective-source namespace plugin stack differs from its profile")
    compatibility = namespace.get("runtimeCompatibility")
    if not isinstance(compatibility, dict) or compatibility.get("ready") is not False:
        raise ValueError("TTW effective-source namespace overstates runtime compatibility")
    return namespace


def load_ttw_resource_order(
    namespace: dict[str, object],
    recipe_path: Path = DEFAULT_REQUIREMENTS_PATH,
) -> TtwResourceOrder:
    """Bind the exact owned archive/root order and zero-byte marker semantics."""

    resolved_recipe = recipe_path.resolve()
    recipe = json.loads(resolved_recipe.read_text(encoding="utf-8"))
    if recipe.get("schema") != "opennv-ttw-profile-requirements/v1":
        raise ValueError(f"Unexpected TTW profile requirements schema: {resolved_recipe}")
    recipe_id = recipe.get("id")
    if not isinstance(recipe_id, str) or not recipe_id:
        raise ValueError("TTW profile recipe has no stable identity")
    raw_contract = recipe.get("effectiveSource")
    if not isinstance(raw_contract, dict):
        raise ValueError("TTW profile recipe has no effective-source order")
    expected_scalars = {
        "schema": EFFECTIVE_SOURCE_ORDER_SCHEMA,
        "sourceRootOrder": SOURCE_ROOT_ORDER_POLICY,
        "looseMemberPrecedence": LOOSE_MEMBER_PRECEDENCE,
        "archiveMemberPrecedence": ARCHIVE_MEMBER_PRECEDENCE,
        "runtimeReady": False,
    }
    for key, expected in expected_scalars.items():
        if raw_contract.get(key) != expected:
            raise ValueError(f"TTW effective-source recipe {key} differs")

    raw_archive_order = raw_contract.get("archiveOrder")
    if (
        not isinstance(raw_archive_order, list)
        or not raw_archive_order
        or not all(isinstance(row, str) and Path(row).name == row for row in raw_archive_order)
    ):
        raise ValueError("TTW effective-source archive order is invalid")
    archive_order = tuple(raw_archive_order)
    if len({name.casefold() for name in archive_order}) != len(archive_order):
        raise ValueError("TTW effective-source archive order contains duplicates")
    namespace_archives = namespace.get("archives")
    if not isinstance(namespace_archives, list) or not all(
        isinstance(row, dict) for row in namespace_archives
    ):
        raise ValueError("TTW effective-source namespace has no archive inventory")
    namespace_archive_names = {
        str(row.get("file", "")).casefold() for row in namespace_archives
    }
    if {name.casefold() for name in archive_order} != namespace_archive_names:
        raise ValueError(
            "TTW effective-source recipe archive order differs from the owned namespace"
        )

    raw_marker_contract = raw_contract.get("overrideMarker")
    if not isinstance(raw_marker_contract, dict):
        raise ValueError("TTW effective-source recipe has no override-marker contract")
    if (
        raw_marker_contract.get("suffix") != ".override"
        or raw_marker_contract.get("requiredBytes") != 0
        or raw_marker_contract.get("pairing")
        != "case-insensitive-same-stem-bsa"
    ):
        raise ValueError("TTW effective-source override-marker semantics differ")
    raw_markers = raw_marker_contract.get("markers")
    if (
        not isinstance(raw_markers, list)
        or not raw_markers
        or not all(
            isinstance(row, str)
            and Path(row).name == row
            and Path(row).suffix.casefold() == ".override"
            for row in raw_markers
        )
    ):
        raise ValueError("TTW effective-source override-marker order is invalid")
    override_markers = tuple(raw_markers)
    if len({name.casefold() for name in override_markers}) != len(override_markers):
        raise ValueError("TTW effective-source override markers contain duplicates")
    namespace_markers = namespace.get("overrideMarkers")
    if not isinstance(namespace_markers, list) or not all(
        isinstance(row, dict) for row in namespace_markers
    ):
        raise ValueError("TTW effective-source namespace has no override-marker inventory")
    marker_rows = {
        str(row.get("file", "")).casefold(): row for row in namespace_markers
    }
    if set(marker_rows) != {name.casefold() for name in override_markers}:
        raise ValueError(
            "TTW effective-source recipe override markers differ from the owned namespace"
        )
    archive_names = {name.casefold() for name in archive_order}
    for marker in override_markers:
        paired_archive = f"{Path(marker).stem}.bsa"
        if paired_archive.casefold() not in archive_names:
            raise ValueError(
                f"TTW override marker has no same-stem archive: {marker}"
            )
        row = marker_rows[marker.casefold()]
        if row.get("bytes") != 0 or row.get("sha256") != hashlib.sha256(b"").hexdigest():
            raise ValueError(f"TTW override marker is not an exact empty file: {marker}")
    return TtwResourceOrder(
        resolved_recipe,
        recipe_id,
        archive_order,
        override_markers,
    )


def _editor_id(record: Record) -> str | None:
    values = [
        zstring(row.data) for row in iter_subrecords(record) if row.signature == "EDID"
    ]
    if len(values) > 1:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} has duplicate EDID values"
        )
    return values[0] if values else None


class EffectiveRecords:
    """Effective record versions keyed by (origin master, local FormID)."""

    def __init__(
        self,
        contexts: tuple[PluginContext, ...],
        source_root_indices: dict[str, int],
        indices: dict[str, int],
        signatures: frozenset[str] | None = None,
    ):
        self.contexts = contexts
        self.source_root_indices = source_root_indices
        self.indices = indices
        self.versions: dict[str, list[RecordVersion]] = {}
        for context in contexts:
            local: set[str] = set()
            for record in iter_plugin_records(context.path, signatures):
                key = context.form_key(record.form_id)
                folded = key.text.casefold()
                if folded in local:
                    raise ValueError(
                        f"{context.name} contains duplicate admitted record {key.text}"
                    )
                local.add(folded)
                self.versions.setdefault(folded, []).append(
                    RecordVersion(
                        context,
                        source_root_indices[context.name.casefold()],
                        record,
                    )
                )
        self.winners = {key: rows[-1] for key, rows in self.versions.items()}

    def resolution(self, origin_master: str, local_form_id: int) -> dict[str, object]:
        if (
            isinstance(local_form_id, bool)
            or not isinstance(local_form_id, int)
            or not 0 <= local_form_id <= FORM_ID_OBJECT_MASK
        ):
            raise ValueError(f"Invalid stable local FormID: {local_form_id!r}")
        canonical_owner = next(
            (
                context.name
                for context in self.contexts
                if context.name.casefold() == origin_master.casefold()
            ),
            None,
        )
        if canonical_owner is None:
            raise ValueError(f"TTW origin master is outside the active stack: {origin_master}")
        form_key = f"{canonical_owner}:{local_form_id:06x}"
        rows = self.versions.get(form_key.casefold())
        if not rows:
            raise ValueError(f"TTW effective FormKey is absent: {form_key}")
        return {
            "formKey": form_key,
            "runtimeFormId": runtime_form_id(
                parse_form_key(form_key),
                self.indices,
            ),
            "winner": self._version_row(rows[-1]),
            "overriddenVersions": [self._version_row(row) for row in rows[:-1]],
        }

    def contract(
        self,
        definition: dict[str, object],
        *,
        expected_editor_id: str | None = None,
    ) -> dict[str, object]:
        key = parse_form_key(str(definition["formKey"]))
        resolution = self.resolution(key.owner_plugin, key.object_id)
        rows = self.versions[resolution["formKey"].casefold()]
        winner = rows[-1]
        record = winner.record
        editor_id = _editor_id(record)
        expected_editor = expected_editor_id or definition.get("editorId")
        if record.flags & DELETED_RECORD_FLAG:
            raise ValueError(f"TTW effective FormKey is deleted: {key.text}")
        if record.signature != definition.get("recordType"):
            raise ValueError(f"TTW effective record type differs: {key.text}")
        if expected_editor is not None and (editor_id or "").casefold() != str(
            expected_editor
        ).casefold():
            raise ValueError(f"TTW effective editor identity differs: {key.text}")
        expected_winner = definition.get("winnerPlugin")
        if expected_winner is not None and winner.context.name.casefold() != str(
            expected_winner
        ).casefold():
            raise ValueError(f"TTW effective winner differs: {key.text}")
        parent_key = None
        raw_parent = cell_parent_form_id(record)
        if raw_parent is not None:
            parent_key = winner.context.form_key(raw_parent).text
        expected_parent = definition.get("parentCell")
        if expected_parent is not None and (parent_key or "").casefold() != str(
            expected_parent
        ).casefold():
            raise ValueError(f"TTW effective parent CELL differs: {key.text}")
        return {
            **resolution,
            "recordType": record.signature,
            "editorId": editor_id,
            "parentCellFormKey": parent_key,
        }

    @staticmethod
    def _version_row(version: RecordVersion) -> dict[str, object]:
        return {
            "plugin": version.context.name,
            "loadOrderIndex": version.context.load_order_index,
            "sourceRootIndex": version.source_root_index,
            "pluginSha256": version.context.sha256,
            "recordSha256": hashlib.sha256(version.record.data).hexdigest(),
            "flags": version.record.flags,
        }

    def winner(self, form_key: str) -> RecordVersion:
        row = self.winners.get(form_key.casefold())
        if row is None:
            raise ValueError(f"TTW effective FormKey is absent: {form_key}")
        return row


def _case_insensitive_descendant(root: Path, logical_path: str) -> Path | None:
    parts = PurePosixPath(logical_path.replace("\\", "/")).parts
    current = root
    for index, part in enumerate(parts):
        if not current.is_dir():
            return None
        matches = [
            path for path in current.iterdir() if path.name.casefold() == part.casefold()
        ]
        if len(matches) > 1:
            raise ValueError(
                f"TTW source root has ambiguous case-insensitive member: {logical_path}"
            )
        if not matches:
            return None
        current = matches[0]
        if index < len(parts) - 1 and not current.is_dir():
            return None
    return current if current.is_file() else None


class EffectiveMembers:
    """Resolve one member through exact BSA-marker and loose-root precedence."""

    def __init__(
        self,
        roots: tuple[Path, ...],
        archives: tuple[TtwArchiveSource, ...],
        override_archives: frozenset[str],
    ):
        if not roots:
            raise ValueError("TTW member resolver has no source roots")
        if len({entry.name.casefold() for entry in archives}) != len(archives):
            raise ValueError("TTW member resolver contains duplicate archives")
        archive_names = {entry.name.casefold() for entry in archives}
        if not override_archives <= archive_names:
            raise ValueError("TTW member resolver has an unpaired override marker")
        self.roots = roots
        self.archives = archives
        self.override_archives = override_archives

    def resolve(self, logical_path: str) -> ResolvedTtwMember:
        requested = canonical_member_path(logical_path)
        versions: list[tuple[dict[str, object], bytes]] = []
        winner_index: int | None = None
        for archive_order_index, source in enumerate(self.archives):
            if requested not in source.archive.members:
                continue
            extracted = source.archive.extract(requested)
            has_override_marker = source.name.casefold() in self.override_archives
            if winner_index is None:
                disposition = "initial-containing-archive-wins"
                winner_index = len(versions)
            elif has_override_marker:
                disposition = "same-stem-override-marker-replaces-earlier-archive"
                winner_index = len(versions)
            else:
                disposition = "unmarked-archive-cannot-replace-earlier-member"
            versions.append(
                (
                    self._archive_version(
                        source,
                        archive_order_index,
                        extracted,
                        has_override_marker,
                        disposition,
                    ),
                    extracted.data,
                )
            )
        normalized_loose = requested.replace("\\", "/")
        for source_root_index, root in enumerate(self.roots):
            path = _case_insensitive_descendant(root, normalized_loose)
            if path is None:
                continue
            payload = path.read_bytes()
            versions.append(
                (
                    {
                        "kind": "loose",
                        "sourceRootIndex": source_root_index,
                        "source": str(path.resolve()),
                        "bytes": len(payload),
                        "sha256": hashlib.sha256(payload).hexdigest(),
                    },
                    payload,
                )
            )
            winner_index = len(versions) - 1
        if not versions:
            raise FileNotFoundError(f"TTW effective member not found: {requested}")
        if winner_index is None:
            raise ValueError(f"TTW effective member has no winner: {requested}")
        return ResolvedTtwMember(
            requested,
            versions[winner_index][1],
            versions[winner_index][0],
            tuple(
                row
                for index, (row, _payload) in enumerate(versions)
                if index != winner_index
            ),
        )

    @staticmethod
    def _archive_version(
        source: TtwArchiveSource,
        archive_order_index: int,
        member: ExtractedMember,
        has_override_marker: bool,
        disposition: str,
    ) -> dict[str, object]:
        return {
            "kind": "bsa",
            "archive": source.name,
            "archiveOrderIndex": archive_order_index,
            "hasSameStemOverrideMarker": has_override_marker,
            "memberPrecedenceDisposition": disposition,
            "sourceRootIndex": source.source_root_index,
            "archiveSha256": source.sha256,
            "memberBytes": len(member.data),
            "memberSha256": member.sha256,
            "archiveOffset": member.archive_offset,
            "storedBytes": member.stored_bytes,
            "compressed": member.compressed,
        }


class TtwEffectiveSource:
    """Bounded source adapter consumed by owned profile compilers."""

    def __init__(
        self,
        profile_path: Path,
        namespace_path: Path,
        profile: dict[str, object],
        namespace: dict[str, object],
        records: EffectiveRecords,
        members: EffectiveMembers | None,
        resource_order: TtwResourceOrder,
    ):
        self.profile_path = profile_path.resolve()
        self.namespace_path = namespace_path.resolve()
        self.profile = profile
        self.namespace = namespace
        self.records = records
        self.members = members
        self.resource_order = resource_order

    def compiler_contract(self) -> dict[str, object]:
        payload = {
            "schema": COMPILER_SOURCE_SCHEMA,
            "pluginStackId": self.profile["pluginStackId"],
            "saveCompatibilityId": self.profile["saveCompatibilityId"],
            "sourceProfileSha256": file_sha256(self.profile_path),
            "sourceNamespaceSha256": file_sha256(self.namespace_path),
            "recordResolutionPolicy": RECORD_RESOLUTION_POLICY,
            "resourceOrder": self.resource_order.contract(),
        }
        digest = hashlib.sha256(
            CACHE_ID_PREFIX
            + json.dumps(
                payload,
                sort_keys=True,
                separators=(",", ":"),
            ).encode("utf-8")
        ).hexdigest()
        return {
            **payload,
            "cacheCompatibilityId": f"{CACHE_NAMESPACE}:{digest}",
            "standaloneFallout3ProfileAccepted": False,
            "standaloneFallout3CacheReused": False,
            "standaloneNewVegasProfileAccepted": False,
            "standaloneNewVegasCacheReused": False,
            "runtimeReady": False,
        }


def load_ttw_effective_record_source(
    profile_path: Path,
    namespace_path: Path,
    signatures: frozenset[str],
    resource_recipe_path: Path = DEFAULT_REQUIREMENTS_PATH,
) -> TtwEffectiveSource:
    """Load a records-only compiler adapter without indexing owned archives."""

    if not signatures:
        raise ValueError("TTW compiler source must declare admitted record signatures")
    profile, roots, contexts, indices = validated_ttw_stack(profile_path)
    namespace = validate_ttw_source_namespace(
        namespace_path,
        profile_path,
        profile,
    )
    resource_order = load_ttw_resource_order(namespace, resource_recipe_path)
    source_root_indices = {
        str(row["file"]).casefold(): int(row["sourceRootIndex"])
        for row in profile["plugins"]
    }
    records = EffectiveRecords(
        contexts,
        source_root_indices,
        indices,
        signatures,
    )
    return TtwEffectiveSource(
        profile_path,
        namespace_path,
        profile,
        namespace,
        records,
        None,
        resource_order,
    )


def load_ttw_effective_source(
    profile_path: Path,
    namespace_path: Path,
    signatures: frozenset[str],
    resource_recipe_path: Path = DEFAULT_REQUIREMENTS_PATH,
) -> TtwEffectiveSource:
    """Load records and indexed archives without writing or extracting owned files."""

    source = load_ttw_effective_record_source(
        profile_path,
        namespace_path,
        signatures,
        resource_recipe_path,
    )
    profile = source.profile
    roots = tuple(Path(row).resolve() for row in profile["sourceRoots"])
    namespace = source.namespace
    resource_order = source.resource_order

    archive_rows = namespace.get("archives")
    if not isinstance(archive_rows, list) or not all(
        isinstance(row, dict) for row in archive_rows
    ):
        raise ValueError("TTW effective-source namespace has no archive inventory")
    rows_by_name = {str(row.get("file", "")).casefold(): row for row in archive_rows}
    if len(rows_by_name) != len(archive_rows):
        raise ValueError("TTW effective-source namespace repeats an archive")
    archives: list[TtwArchiveSource] = []
    for requested_name in resource_order.archive_order:
        row = rows_by_name[requested_name.casefold()]
        root_index = row.get("sourceRootIndex")
        if (
            not isinstance(root_index, int)
            or isinstance(root_index, bool)
            or not 0 <= root_index < len(roots)
        ):
            raise ValueError(f"TTW archive has invalid source root: {requested_name}")
        matches = [
            path
            for path in roots[root_index].iterdir()
            if path.is_file() and path.name.casefold() == requested_name.casefold()
        ]
        if len(matches) != 1:
            raise FileNotFoundError(
                f"TTW effective archive does not resolve uniquely: {requested_name}"
            )
        path = matches[0]
        expected_sha256 = row.get("sha256")
        expected_bytes = row.get("bytes")
        if (
            not _is_sha256(expected_sha256)
            or not isinstance(expected_bytes, int)
            or isinstance(expected_bytes, bool)
            or path.stat().st_size != expected_bytes
            or file_sha256(path) != expected_sha256
        ):
            raise ValueError(f"TTW effective archive changed: {requested_name}")
        archives.append(
            TtwArchiveSource(
                str(row["file"]),
                path,
                root_index,
                str(expected_sha256),
                expected_bytes,
                BsaArchive(path),
            )
        )
    return TtwEffectiveSource(
        profile_path,
        namespace_path,
        profile,
        namespace,
        source.records,
        EffectiveMembers(
            roots,
            tuple(archives),
            resource_order.override_archives,
        ),
        resource_order,
    )


# Compatibility aliases retained while the bounded TTW opening compiler moves
# onto this reusable source boundary.
_validated_stack = validated_ttw_stack
_validated_source_namespace = validate_ttw_source_namespace
