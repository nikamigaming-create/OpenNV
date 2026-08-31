#!/usr/bin/env python3
"""Materialize the three effective TTW CG00 stage-10 actors.

This is deliberately separate from the standalone Fallout 3 actor recipes.
The expanded TTW closure is the identity authority; Bethesda payloads remain
read-only and the generated glTF/cache remains local and disposable.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from dataclasses import replace
from pathlib import Path

from actor_catalog import (
    ActorCatalog,
    ActorItem,
    ActorReference,
    _actor,
    _armor,
    _part,
    _race,
    _subrecords,
)
from actor_gltf import actor_texture_paths
from bsa_archive import ExtractedMember, canonical_member_path
from plugin_records import iter_subrecords
from plugin_stack import file_sha256, parse_form_key
from nif_decoder import decode_nif
from pyffi.formats.nif import NifFormat  # type: ignore
from prepare_actor import (
    ActorPreparationContext,
    ActorRuntimeSurfaceProjection,
    prepare_actor,
)
from runtime_configuration import load_runtime_configuration
from ttw_effective_source import load_ttw_effective_source
from ttw_fo3_stage10_resource_closure import (
    ADMITTED_RECORD_TYPES,
    SCHEMA as CLOSURE_SCHEMA,
    STATUS as CLOSURE_STATUS,
    _canonical_sha256,
    _record_identity,
    _values,
)


SCHEMA = "opennv-ttw-fo3-cg00-stage10-actor-set/v1"
STATUS = "effective-ttw-actors-materialized-for-exact-live-stage10"
ROLE_RECIPES = {
    "father": "fo3-vault101-dad-actor-v1",
    "doctor": "fo3-vault101-doctor-li-actor-v1",
    "mother": "fo3-vault101-mom-actor-v1",
}
ROLE_ANIMATION_STEMS = {
    "father": "cg00dadsection",
    "doctor": "cg00drlisection",
    "mother": "cg00momsection",
}
CELL_FORM_ID = 0x00028138
IDLE_PATH = r"meshes\characters\_male\locomotion\mtidle.kf"
SURFACE_CONTRACT_SCHEMA = "opennv-ttw-fo3-cg00-stage10-retail-surface-depth/v1"
SURFACE_CONTRACT_STATUS = "exact-live-retail-surface-depth-distribution-derived"
ROLE_SURFACE_PROJECTIONS = {
    "father": {
        "outfit": (
            r"armor\projectpuritydoctor\outfitm.nif",
            (
                "pipboyoff:0", "Arms:0", "limbcaps:0", "bodycaps:0",
                "UpperBody:0", "meathead:0", "meatneck:0",
            ),
        ),
        "left": (
            r"armor\projectpuritydoctor\glovelm.nif",
            "005a5714e46411a54d5afd2990d7d20c4039571682d7a9e3f2d9137e4a0cec56",
            ("leftglove",),
        ),
        "right": (
            r"armor\projectpuritydoctor\gloverm.nif",
            "10094f4c73c22a31aeeef0c0e9012db735f4f7cc822d38419a1f70fa32173902",
            ("rightglove",),
        ),
    },
    "doctor": {
        "outfit": (
            r"armor\doctorli\f\outfitf.nif",
            (
                "UpperBody", "Arms", "UpperBody1", "MeatCapBody",
                "MeatCapLimbs", "PipBoyOff", "Arms01", "bodymeat", "headmeat",
            ),
        ),
        "left": (
            r"armor\doctorli\f\glovel.nif",
            "4844912dd345643465f7829895c295d3ab38b949da3ed8524ad908bbbc7d2a13",
            ("leftglove",),
        ),
        "right": (
            r"armor\doctorli\f\glover.nif",
            "b6a8ff14ebb728fb880e90552a7a472f23234380cc46feff4c6bccdb6257b276",
            ("rightglove",),
        ),
    },
    "mother": {
        "outfit": (
            r"armor\chargen\birthskirt.nif",
            ("UpperBody:0", "Arms01:0", "BirthSkirt:0"),
        ),
        "left": (
            r"characters\_male\femalelefthand.nif",
            "2271148feafb468252d5c57c734fe98d2cfe0546f3484fd26ef81b8ab4c4e006",
            ("LeftHand:0",),
        ),
        "right": (
            r"characters\_male\femalerighthand.nif",
            "76eba07ada2c703ae50efbed8b728823dd96078d385541762f167af09404f526",
            ("RightHand:0",),
        ),
    },
}


def _sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def _stable_id(form_key: str) -> int:
    return parse_form_key(form_key).object_id


def _read_closure(path: Path) -> tuple[dict[str, object], bytes]:
    payload = path.resolve().read_bytes()
    closure = json.loads(payload)
    expanded = closure.get("expandedClosure")
    identity = closure.get("identity")
    if (
        closure.get("schema") != CLOSURE_SCHEMA
        or closure.get("status") != CLOSURE_STATUS
        or closure.get("campaign") != "Fallout3"
        or closure.get("edition") != "TTW"
        or closure.get("stage") != {"questEditorId": "CG00", "stage": 10}
        or closure.get("resourceClosureReady") is not True
        or closure.get("standaloneArtifactsAccepted") is not False
        or not isinstance(expanded, dict)
        or not isinstance(identity, dict)
        or expanded.get("recordCount") != len(expanded.get("records", []))
        or expanded.get("memberCount") != len(expanded.get("members", []))
        or identity.get("expandedRecordClosureSha256")
        != _canonical_sha256(expanded.get("records", []))
        or identity.get("expandedMemberClosureSha256")
        != _canonical_sha256(expanded.get("members", []))
    ):
        raise ValueError("TTW CG00 stage-10 actor closure identity differs")
    return closure, payload


class _EffectiveArchive:
    """BsaArchive-compatible view containing only effective closed winners."""

    def __init__(
        self,
        source: object,
        archive: Path,
        archive_sha256: str,
        members: dict[str, dict[str, object]],
    ) -> None:
        self._source = source
        self.archive = archive.resolve()
        self._archive_sha256 = archive_sha256
        self._contracts = members
        self.members = frozenset(members)

    def extract(self, logical_path: str) -> ExtractedMember:
        requested = canonical_member_path(logical_path)
        expected = self._contracts.get(requested)
        if expected is None:
            raise FileNotFoundError(requested)
        resolved = self._source.members.resolve(requested)
        if resolved.contract() != expected:
            raise ValueError(f"TTW effective actor member changed: {requested}")
        winner = resolved.winner
        if (
            winner.get("kind") != "bsa"
            or str(winner.get("archive", "")).casefold()
            != self.archive.name.casefold()
            or winner.get("archiveSha256") != self._archive_sha256
        ):
            raise ValueError(f"TTW effective actor archive winner differs: {requested}")
        return ExtractedMember(
            requested,
            resolved.data,
            bool(winner["compressed"]),
            int(winner["archiveOffset"]),
            int(winner["storedBytes"]),
            self.archive.name,
            self._archive_sha256,
        )


def _source_roots(source: object) -> tuple[Path, ...]:
    rows = source.profile.get("sourceRoots")
    if not isinstance(rows, list) or not rows:
        raise ValueError("TTW profile source roots are absent")
    return tuple(Path(str(row)).resolve() for row in rows)


def _archive_path(roots: tuple[Path, ...], winner: dict[str, object]) -> Path:
    index = winner.get("sourceRootIndex")
    if not isinstance(index, int) or not 0 <= index < len(roots):
        raise ValueError("TTW actor member source-root index differs")
    name = str(winner.get("archive", ""))
    matches = [
        path for path in roots[index].iterdir()
        if path.is_file() and path.name.casefold() == name.casefold()
    ]
    if len(matches) != 1 or file_sha256(matches[0]) != winner.get("archiveSha256"):
        raise ValueError(f"TTW actor archive identity changed: {name}")
    return matches[0].resolve()


def _member_contracts(
    closure: dict[str, object],
    source: object,
) -> dict[str, dict[str, object]]:
    rows = {
        canonical_member_path(str(row["logicalPath"])): row
        for row in closure["expandedClosure"]["members"]
    }
    # mtidle is the common compiler seed, while the five package animations
    # are already members of the expanded stage closure.
    if IDLE_PATH not in rows:
        rows[IDLE_PATH] = source.members.resolve(IDLE_PATH).contract()
    for graph in closure["actors"].values():
        owner = parse_form_key(str(graph["base"]["formKey"]))
        sex = "female" if graph["female"] else "male"
        body_mod = canonical_member_path(
            f"textures\\characters\\bodymods\\{owner.owner_plugin}\\"
            f"{owner.object_id:08x}modbody{sex}.dds"
        )
        if body_mod not in rows:
            try:
                rows[body_mod] = source.members.resolve(body_mod).contract()
            except FileNotFoundError:
                pass
        head_path = canonical_member_path(
            str(graph["raceModels"][0]["member"]["logicalPath"])
        )
        if not head_path.endswith(".nif"):
            raise ValueError("TTW actor race head model is not a NIF")
        texture_basis = head_path[:-4] + ".egt"
        if texture_basis not in rows:
            rows[texture_basis] = source.members.resolve(texture_basis).contract()
        component_models = [
            str(row["member"]["logicalPath"])
            for row in graph["raceModels"][:7]
        ]
        component_models.extend(
            str(row["member"]["logicalPath"])
            for part in graph["headParts"]
            for row in part["models"]
        )
        hair_models = [
            str(row["member"]["logicalPath"])
            for row in graph["hair"]["models"]
        ]
        for model_path in component_models:
            canonical = canonical_member_path(model_path)
            if not canonical.endswith(".nif"):
                continue
            for suffix in (".egm", ".tri"):
                companion = canonical[:-4] + suffix
                if companion in rows:
                    continue
                try:
                    rows[companion] = source.members.resolve(companion).contract()
                except FileNotFoundError:
                    if suffix == ".egm":
                        raise
        for model_path in hair_models:
            canonical = canonical_member_path(model_path)
            if not canonical.endswith(".nif"):
                continue
            for suffix in ("hat.egm", "nohat.egm", ".tri"):
                companion = canonical[:-4] + suffix
                if companion in rows:
                    continue
                try:
                    rows[companion] = source.members.resolve(companion).contract()
                except FileNotFoundError:
                    # Only the selected source shape needs an EGM; the exporter
                    # will fail closed if the selected one is absent.
                    pass
    for projection in ROLE_SURFACE_PROJECTIONS.values():
        for side in ("left", "right"):
            logical_path = canonical_member_path(
                f"meshes\\{projection[side][0]}"
            )
            resolved = source.members.resolve(logical_path)
            if resolved.contract()["sha256"] != projection[side][1]:
                raise ValueError(
                    f"TTW stage-10 exact hand model changed: {logical_path}"
                )
            rows[logical_path] = resolved.contract()
            document = decode_nif(resolved.data).document
            texture_paths = {
                texture_path
                for shape in document.get_global_iterator()
                if isinstance(shape, (NifFormat.NiTriShape, NifFormat.NiTriStrips))
                for texture_path in actor_texture_paths(list(shape.properties))
                if texture_path
            }
            for texture_path in sorted(texture_paths):
                texture = source.members.resolve(texture_path)
                rows[canonical_member_path(texture_path)] = texture.contract()
    return rows


def _archive_views(
    contracts: dict[str, dict[str, object]],
    source: object,
    roots: tuple[Path, ...],
    prefix: str,
) -> tuple[_EffectiveArchive, ...]:
    grouped: dict[tuple[str, str], dict[str, dict[str, object]]] = {}
    winner_by_group: dict[tuple[str, str], dict[str, object]] = {}
    for logical_path, contract in contracts.items():
        if not logical_path.startswith(prefix):
            continue
        winner = contract.get("winner")
        if not isinstance(winner, dict) or winner.get("kind") != "bsa":
            raise ValueError(
                f"TTW stage-10 actor requires a non-BSA effective member: {logical_path}"
            )
        key = (str(winner["archive"]).casefold(), str(winner["archiveSha256"]))
        grouped.setdefault(key, {})[logical_path] = contract
        winner_by_group[key] = winner
    result = []
    for key in sorted(grouped):
        winner = winner_by_group[key]
        result.append(
            _EffectiveArchive(
                source,
                _archive_path(roots, winner),
                str(winner["archiveSha256"]),
                grouped[key],
            )
        )
    if not result:
        raise ValueError(f"TTW actor closure has no {prefix} members")
    return tuple(result)


def _record_with_stable_id(source: object, form_key: str):
    version = source.records.winner(form_key)
    return version, replace(version.record, form_id=_stable_id(form_key))


def _appearance_part(source: object, identity: dict[str, object]):
    form_key = str(identity["formKey"])
    version, record = _record_with_stable_id(source, form_key)
    parsed = _part(record, _subrecords(record))
    if _record_identity(source, form_key) != identity:
        raise ValueError(f"TTW actor appearance record changed: {form_key}")
    return parsed, version


def _actor_catalog_for_role(
    source: object,
    closure: dict[str, object],
    projection: dict[str, object],
    role: str,
) -> ActorCatalog:
    graph = closure["actors"][role]
    base_key = str(graph["base"]["formKey"])
    base_version, base_record = _record_with_stable_id(source, base_key)
    actor = _actor(base_record, _subrecords(base_record))
    race_key = str(graph["race"]["formKey"])
    race_version, race_record = _record_with_stable_id(source, race_key)
    race = _race(race_record, _subrecords(race_record))
    hair, _ = _appearance_part(source, graph["hair"]["record"])
    eyes, _ = _appearance_part(source, graph["eyes"]["record"])
    head_parts = [
        _appearance_part(source, row["record"])[0]
        for row in graph["headParts"]
    ]
    outfits = []
    for row in graph["outfit"]:
        form_key = str(row["record"]["formKey"])
        _version, record = _record_with_stable_id(source, form_key)
        outfits.append(_armor(record, _subrecords(record)))

    actor = replace(
        actor,
        form_id=_stable_id(base_key),
        skeleton_path=str(graph["skeleton"]["member"]["logicalPath"]),
        race_form_id=_stable_id(race_key),
        hair_form_id=hair.form_id,
        eyes_form_id=eyes.form_id,
        head_part_form_ids=tuple(part.form_id for part in head_parts),
        inventory=tuple(ActorItem(outfit.form_id, 1) for outfit in outfits),
    )
    participant = next(
        row for row in projection["earlyBirthSequence"]["sceneParticipants"]
        if row["role"] == role
    )
    reference_identity = participant["reference"]["sourceIdentity"]
    reference_key = str(reference_identity["formKey"])
    reference_version = source.records.winner(reference_key)
    reference_values = _values(reference_version)
    enable_parent = None
    xesp = reference_values.get("XESP", [])
    if xesp:
        if len(xesp) != 1 or len(xesp[0]) < 4:
            raise ValueError(f"TTW actor reference XESP differs: {reference_key}")
        enable_parent = _stable_id(
            reference_version.context.form_key(struct.unpack_from("<I", xesp[0])[0]).text
        )
    transform = participant["reference"]["authoredTransform"]
    reference = ActorReference(
        _stable_id(reference_key),
        "ACHR",
        CELL_FORM_ID,
        actor.form_id,
        int(reference_identity["winner"]["flags"]),
        tuple(float(value) for value in transform["positionGameUnits"]),
        tuple(float(value) for value in transform["rotationRadians"]),
        float(transform["scale"]),
        enable_parent,
    )
    catalog = ActorCatalog(
        {actor.form_id: actor},
        {},
        [reference],
        {race.form_id: race},
        {part.form_id: part for part in (hair, eyes, *head_parts)},
        {outfit.form_id: outfit for outfit in outfits},
        {},
    )
    catalog.record_data_sha256 = {
        "NPC_": {actor.form_id: str(graph["base"]["winner"]["recordSha256"])},
        "ACHR": {
            reference.form_id: str(reference_identity["winner"]["recordSha256"])
        },
    }
    if _record_identity(source, base_key) != graph["base"]:
        raise ValueError(f"TTW actor base record changed: {base_key}")
    if _record_identity(source, race_key) != graph["race"]:
        raise ValueError(f"TTW actor race record changed: {race_key}")
    return catalog


def _recipe_document(
    template: dict[str, object],
    data_root: Path,
    master: Path,
    mesh_archives: tuple[_EffectiveArchive, ...],
    texture_archives: tuple[_EffectiveArchive, ...],
) -> dict[str, object]:
    if any(path.archive.parent != data_root for path in (*mesh_archives, *texture_archives)):
        raise ValueError("TTW stage-10 actor winners span another source root")
    result = dict(template)
    result["master"] = {"file": master.name, "sha256": file_sha256(master)}
    result["meshesArchive"] = {
        "file": mesh_archives[0].archive.name,
        "sha256": file_sha256(mesh_archives[0].archive),
    }
    result["additionalMeshesArchives"] = [
        {"file": row.archive.name, "sha256": file_sha256(row.archive)}
        for row in mesh_archives[1:]
    ]
    result["textureArchives"] = [
        {"file": row.archive.name, "sha256": file_sha256(row.archive)}
        for row in texture_archives
    ]
    return result


def materialize_ttw_stage10_actors(
    closure_path: Path,
    surface_contract_path: Path,
    output_root: Path,
) -> dict[str, object]:
    closure, closure_payload = _read_closure(closure_path)
    surface_contract_payload = surface_contract_path.resolve().read_bytes()
    surface_contract = json.loads(surface_contract_payload)
    if (
        surface_contract.get("schema") != SURFACE_CONTRACT_SCHEMA
        or surface_contract.get("status") != SURFACE_CONTRACT_STATUS
        or surface_contract.get("campaign") != "Fallout3"
        or surface_contract.get("edition") != "TTW"
        or surface_contract.get("stage") != 10
    ):
        raise ValueError("TTW stage-10 exact surface authority differs")
    surface_contract_sha256 = _sha256_bytes(surface_contract_payload)
    identity = closure["identity"]
    profile_path = Path(str(identity["sourceProfile"]["file"])).resolve()
    namespace_path = Path(str(identity["sourceNamespace"]["file"])).resolve()
    if (
        file_sha256(profile_path) != identity["sourceProfile"]["sha256"]
        or file_sha256(namespace_path) != identity["sourceNamespace"]["sha256"]
    ):
        raise ValueError("TTW actor source profile or namespace changed")
    source = load_ttw_effective_source(
        profile_path,
        namespace_path,
        ADMITTED_RECORD_TYPES,
    )
    if source.members is None:
        raise ValueError("TTW effective member overlay is absent")
    compiler = source.compiler_contract()
    if (
        compiler["pluginStackId"] != identity["pluginStackId"]
        or compiler["saveCompatibilityId"] != identity["saveCompatibilityId"]
    ):
        raise ValueError("TTW actor effective source namespace differs")
    projection_path = Path(str(identity["projection"]["path"])).resolve()
    if file_sha256(projection_path) != identity["projection"]["sha256"]:
        raise ValueError("TTW actor projection identity changed")
    projection = json.loads(projection_path.read_bytes())
    contracts = _member_contracts(closure, source)
    roots = _source_roots(source)
    mesh_archives = _archive_views(contracts, source, roots, "meshes\\")
    texture_archives = _archive_views(contracts, source, roots, "textures\\")
    master = source.records.winner(
        str(closure["actors"]["father"]["base"]["formKey"])
    ).context.path.resolve()
    data_root = master.parent
    configuration = load_runtime_configuration()
    actors: dict[str, object] = {}
    recipes_root = Path(__file__).resolve().parents[1] / "recipes"
    for role, recipe_id in ROLE_RECIPES.items():
        template = json.loads(
            (recipes_root / f"{recipe_id}.json").read_text(encoding="utf-8")
        )
        recipe = _recipe_document(
            template,
            data_root,
            master,
            mesh_archives,
            texture_archives,
        )
        context = ActorPreparationContext(
            configuration,
            tuple(
                (data_root / str(row["file"]), str(row["sha256"]))
                for row in (
                    recipe["master"],
                    recipe["meshesArchive"],
                    *recipe.get("additionalMeshesArchives", []),
                    *recipe["textureArchives"],
                )
            ),
            master,
            _actor_catalog_for_role(source, closure, projection, role),
            mesh_archives,
            texture_archives,
        )
        stem = ROLE_ANIMATION_STEMS[role]
        animations = tuple(
            rf"meshes\characters\_male\idleanims\{stem}{index:02d}.kf"
            for index in range(5)
        )
        for path in animations:
            resolved = source.members.resolve(path)
            expected = contracts.get(path)
            if expected is None or resolved.contract() != expected:
                raise ValueError(f"TTW stage-10 package animation changed: {path}")
        manifest = prepare_actor(
            data_root,
            output_root,
            recipe_id,
            recipe_document=recipe,
            preparation_context=context,
            runtime_animation_paths=animations,
            runtime_surface_projection=ActorRuntimeSurfaceProjection(
                authority_path=str(surface_contract_path.resolve()),
                authority_sha256=surface_contract_sha256,
                included_shapes_by_model=(
                    ROLE_SURFACE_PROJECTIONS[role]["outfit"],
                    (
                        ROLE_SURFACE_PROJECTIONS[role]["left"][0],
                        ROLE_SURFACE_PROJECTIONS[role]["left"][2],
                    ),
                    (
                        ROLE_SURFACE_PROJECTIONS[role]["right"][0],
                        ROLE_SURFACE_PROJECTIONS[role]["right"][2],
                    ),
                ),
                left_hand_model_path=ROLE_SURFACE_PROJECTIONS[role]["left"][0],
                left_hand_model_sha256=ROLE_SURFACE_PROJECTIONS[role]["left"][1],
                right_hand_model_path=ROLE_SURFACE_PROJECTIONS[role]["right"][0],
                right_hand_model_sha256=ROLE_SURFACE_PROJECTIONS[role]["right"][1],
                include_dismember_cap_shapes=True,
            ),
        )
        manifest_path = Path(str(manifest["manifest"])).resolve()
        actors[role] = {
            "actorScene": str(manifest_path),
            "actorSceneSha256": file_sha256(manifest_path),
            "skeletonLogicalPath": closure["actors"][role]["skeleton"]["member"][
                "logicalPath"
            ],
            "skeletonSha256": closure["actors"][role]["skeleton"]["member"][
                "sha256"
            ],
            "baseFormKey": closure["actors"][role]["base"]["formKey"],
            "referenceFormKey": next(
                row["reference"]["sourceIdentity"]["formKey"]
                for row in projection["earlyBirthSequence"]["sceneParticipants"]
                if row["role"] == role
            ),
        }
    return {
        "schema": SCHEMA,
        "status": STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "stage": 10,
        "sourceAuthority": "effective-ttw-plugin-and-resource-overlay",
        "closure": {
            "path": str(closure_path.resolve()),
            "sha256": _sha256_bytes(closure_payload),
            "expandedRecordClosureSha256": identity["expandedRecordClosureSha256"],
            "expandedMemberClosureSha256": identity["expandedMemberClosureSha256"],
        },
        "retailSurfaceAuthority": {
            "path": str(surface_contract_path.resolve()),
            "sha256": surface_contract_sha256,
        },
        "pluginStackId": identity["pluginStackId"],
        "saveCompatibilityId": identity["saveCompatibilityId"],
        "actors": actors,
        "standaloneActorArtifactsAccepted": False,
        "ownedPayloadsEmbedded": False,
    }


def _main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--closure", type=Path, required=True)
    parser.add_argument("--surface-contract", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    if arguments.output.exists():
        raise FileExistsError(f"Refusing to overwrite TTW actor set: {arguments.output}")
    document = materialize_ttw_stage10_actors(
        arguments.closure,
        arguments.surface_contract,
        arguments.cache_root,
    )
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
