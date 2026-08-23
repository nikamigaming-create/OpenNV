"""Transport Fallout MAP scripts and placed-object/PRO relationships.

The output is a neutral local contract. It does not select 3D substitutions or
create Godot nodes. Loose Et Tu resources override the pinned Fallout 2 DAT2
archive, matching the bounded source profile's effective resource intent.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import struct
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

from dat2_archive import Dat2Archive, canonical_dat2_path
from fo1_profile import MAP_HEADER_SIZE, Fo1ProfileError, parse_map_layout, sha256_path


CONTRACT_SCHEMA = "opennv-fo1-map-object-contract/v1"
OBJECT_TYPE_NAMES = {
    0: "item",
    1: "critter",
    2: "scenery",
    3: "wall",
    4: "tile",
    5: "misc",
}
TYPE_DIRECTORIES = {
    0: "items",
    1: "critters",
    2: "scenery",
    3: "walls",
    4: "tiles",
    5: "misc",
}
ITEM_SUBTYPES = {
    0: "armor",
    1: "container",
    2: "drug",
    3: "weapon",
    4: "ammo",
    5: "misc",
    6: "key",
}
SCENERY_SUBTYPES = {
    0: "door",
    1: "stairs",
    2: "elevator",
    3: "ladder-up",
    4: "ladder-down",
    5: "generic",
}


@dataclass(frozen=True)
class ResourceBytes:
    logical_path: str
    data: bytes
    source: str
    sha256: str


@dataclass(frozen=True)
class Prototype:
    pid: int
    object_type: int
    list_index: int
    filename: str | None
    message_number: int | None
    fid: int | None
    subtype: int | None
    subtype_name: str | None
    source: str
    sha256: str | None


class Fo1ResourceResolver:
    def __init__(
        self,
        ettu_root: Path,
        master_dat: Path,
        additional_archives: list[Path] | None = None,
    ):
        self.override_root = (ettu_root / "mods" / "fo1_base").resolve()
        if not self.override_root.is_dir():
            raise Fo1ProfileError(f"Et Tu fo1_base override root is missing: {self.override_root}")
        self.master_dat = master_dat.resolve()
        self.archives = [Dat2Archive(self.master_dat)] + [
            Dat2Archive(path.resolve()) for path in additional_archives or []
        ]
        self.loose_files = {
            canonical_dat2_path(str(path.relative_to(self.override_root))): path
            for path in self.override_root.rglob("*")
            if path.is_file()
        }
        self.resources: dict[str, ResourceBytes] = {}
        self.prototypes: dict[int, Prototype] = {}
        self.lists: dict[str, list[str]] = {}

    def read(self, logical_path: str) -> ResourceBytes:
        canonical = canonical_dat2_path(logical_path)
        cached = self.resources.get(canonical)
        if cached is not None:
            return cached
        loose = self.loose_files.get(canonical)
        if loose is not None:
            data = loose.read_bytes()
            resource = ResourceBytes(
                canonical,
                data,
                f"ettu-loose:{canonical}",
                hashlib.sha256(data).hexdigest(),
            )
        else:
            member = None
            archive_name = ""
            for archive in self.archives:
                try:
                    member = archive.extract(canonical)
                    archive_name = archive.path.name
                    break
                except FileNotFoundError:
                    continue
            if member is None:
                raise FileNotFoundError(f"Fallout DAT2 member not found: {canonical}")
            resource = ResourceBytes(
                canonical,
                member.data,
                f"fallout2-{Path(archive_name).stem.casefold()}-dat:{canonical}",
                member.sha256,
            )
        self.resources[canonical] = resource
        return resource

    def list_lines(self, logical_path: str) -> list[str]:
        canonical = canonical_dat2_path(logical_path)
        cached = self.lists.get(canonical)
        if cached is not None:
            return cached
        resource = self.read(canonical)
        try:
            text = resource.data.decode("cp1252")
        except UnicodeDecodeError as error:
            raise Fo1ProfileError(f"Fallout list is not cp1252: {canonical}") from error
        lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
        if lines and lines[-1] == "":
            lines.pop()
        self.lists[canonical] = lines
        return lines

    def prototype(self, pid: int) -> Prototype:
        cached = self.prototypes.get(pid)
        if cached is not None:
            return cached
        object_type = (pid >> 24) & 0xFF
        list_index = pid & 0x00FFFFFF
        if object_type not in OBJECT_TYPE_NAMES:
            raise Fo1ProfileError(f"unsupported Fallout PID type {object_type} in {pid:08x}")
        if pid == 0x01000000:
            prototype = Prototype(pid, object_type, list_index, None, None, None, None, None, "builtin", None)
            self.prototypes[pid] = prototype
            return prototype
        if list_index <= 0:
            raise Fo1ProfileError(f"Fallout PID uses invalid one-based list index: {pid:08x}")
        directory = TYPE_DIRECTORIES[object_type]
        list_path = f"proto\\{directory}\\{directory}.lst"
        lines = self.list_lines(list_path)
        if list_index > len(lines):
            raise Fo1ProfileError(
                f"Fallout PID {pid:08x} list index {list_index} exceeds {list_path} ({len(lines)})"
            )
        entry = lines[list_index - 1].split(" ", 1)[0].strip()
        if not entry:
            raise Fo1ProfileError(f"Fallout PID {pid:08x} resolves to an empty PRO list entry")
        pro_path = f"proto\\{directory}\\{entry}"
        resource = self.read(pro_path)
        if len(resource.data) < 0x0C:
            raise Fo1ProfileError(f"Fallout PRO is too short: {pro_path}")
        stored_pid, message_number, fid = struct.unpack_from(">III", resource.data, 0)
        if stored_pid != pid:
            raise Fo1ProfileError(
                f"Fallout PRO PID mismatch for {pro_path}: expected {pid:08x}, got {stored_pid:08x}"
            )
        subtype = None
        subtype_name = None
        if object_type in {0, 2}:
            if len(resource.data) < 0x24:
                raise Fo1ProfileError(f"Fallout typed PRO is too short: {pro_path}")
            subtype = struct.unpack_from(">i", resource.data, 0x20)[0]
            names = ITEM_SUBTYPES if object_type == 0 else SCENERY_SUBTYPES
            if subtype not in names:
                raise Fo1ProfileError(
                    f"unsupported PRO subtype {subtype} for {OBJECT_TYPE_NAMES[object_type]} {pid:08x}"
                )
            subtype_name = names[subtype]
        prototype = Prototype(
            pid,
            object_type,
            list_index,
            entry,
            message_number,
            fid,
            subtype,
            subtype_name,
            resource.source,
            resource.sha256,
        )
        self.prototypes[pid] = prototype
        return prototype

    def art_filename(self, fid: int) -> str | None:
        object_type = (fid >> 24) & 0x0F
        if object_type not in TYPE_DIRECTORIES:
            return None
        art_index = fid & 0x0FFF
        directory = TYPE_DIRECTORIES[object_type]
        lines = self.list_lines(f"art\\{directory}\\{directory}.lst")
        if art_index >= len(lines):
            raise Fo1ProfileError(
                f"Fallout FID {fid:08x} art index {art_index} exceeds art {directory}.lst ({len(lines)})"
            )
        return lines[art_index].split(" ", 1)[0].strip() or None


def _read_i32(data: bytes, offset: int, label: str) -> tuple[int, int]:
    if offset + 4 > len(data):
        raise Fo1ProfileError(f"truncated {label} at 0x{offset:x}")
    return struct.unpack_from(">i", data, offset)[0], offset + 4


def parse_script_section(data: bytes, offset: int) -> tuple[list[dict[str, Any]], int]:
    lists = []
    for list_type in range(5):
        live_count, offset = _read_i32(data, offset, f"script list {list_type} count")
        if live_count < 0:
            raise Fo1ProfileError(f"negative script count for list {list_type}")
        extent_count = (live_count + 15) // 16
        extents = []
        for extent_index in range(extent_count):
            slots = []
            for slot_index in range(16):
                if offset + 4 > len(data):
                    raise Fo1ProfileError("truncated MAP script slot")
                sid = struct.unpack_from(">i", data, offset)[0]
                sid_type = ((sid & 0xFFFFFFFF) >> 24) if sid >= 0 else 0xFF
                record_size = 72 if sid_type == 1 else 68 if sid_type == 2 else 64
                if offset + record_size > len(data):
                    raise Fo1ProfileError("truncated MAP script record")
                slots.append({"slot": slot_index, "sid": f"{sid & 0xFFFFFFFF:08x}", "bytes": record_size})
                offset += record_size
            length, offset = _read_i32(data, offset, "script extent length")
            next_value, offset = _read_i32(data, offset, "script extent next")
            if not 0 <= length <= 16:
                raise Fo1ProfileError(f"invalid MAP script extent length {length}")
            extents.append({"index": extent_index, "length": length, "next": next_value, "slots": slots})
        if sum(extent["length"] for extent in extents) != live_count:
            raise Fo1ProfileError(f"MAP script live-count mismatch for list {list_type}")
        lists.append(
            {
                "type": list_type,
                "liveCount": live_count,
                "extentCount": extent_count,
                "extents": extents,
            }
        )
    return lists, offset


def _is_exit_grid(prototype: Prototype) -> bool:
    message_number = prototype.message_number
    return message_number is not None and (
        message_number in range(1600, 2400, 100) or message_number in range(3100, 4700, 100)
    )


def _instance_extra_count(version: int, prototype: Prototype) -> int:
    if prototype.object_type == 1:
        return 11
    if prototype.object_type == 0:
        return {0: 0, 1: 0, 2: 0, 3: 2, 4: 1, 5: 1, 6: 1}[prototype.subtype]
    if prototype.object_type == 2:
        if prototype.subtype == 0:
            return 1
        if prototype.subtype in {1, 2}:
            return 2
        if prototype.subtype in {3, 4}:
            return 1 if version == 19 else 2
        return 0
    if prototype.object_type == 5 and _is_exit_grid(prototype):
        return 4
    return 0


def parse_map_objects(
    data: bytes,
    offset: int,
    version: int,
    resolver: Fo1ResourceResolver,
) -> tuple[dict[str, Any], int]:
    total_count, offset = _read_i32(data, offset, "total object count")
    if not 0 <= total_count <= 100000:
        raise Fo1ProfileError(f"invalid total MAP object count {total_count}")
    serial = 0

    def read_object(current_offset: int, containing_elevation: int, depth: int) -> tuple[dict[str, Any], int]:
        nonlocal serial
        if depth > 16:
            raise Fo1ProfileError("MAP inventory nesting exceeds 16 levels")
        if current_offset + 84 > len(data):
            raise Fo1ProfileError("truncated MAP object base")
        values = struct.unpack_from(">21i", data, current_offset)
        current_offset += 84
        (
            object_id,
            tile,
            pixel_x,
            pixel_y,
            screen_x,
            screen_y,
            frame,
            rotation,
            fid_signed,
            flags_signed,
            stored_elevation,
            pid_signed,
            combat_id,
            light_distance,
            light_intensity,
            outline,
            sid_signed,
            script_index,
            inventory_length,
            inventory_capacity,
            inventory_pointer,
        ) = values
        fid = fid_signed & 0xFFFFFFFF
        pid = pid_signed & 0xFFFFFFFF
        flags = flags_signed & 0xFFFFFFFF
        sid = sid_signed & 0xFFFFFFFF
        if tile != -1 and not 0 <= tile < 40000:
            raise Fo1ProfileError(f"MAP object {object_id} has invalid tile {tile}")
        if not 0 <= rotation <= 5:
            raise Fo1ProfileError(f"MAP object {object_id} has invalid rotation {rotation}")
        if not 0 <= stored_elevation <= 2:
            raise Fo1ProfileError(f"MAP object {object_id} has invalid elevation {stored_elevation}")
        if depth == 0 and stored_elevation != containing_elevation:
            raise Fo1ProfileError(
                f"MAP object {object_id} elevation {stored_elevation} does not match list {containing_elevation}"
            )
        if not 0 <= inventory_length <= 10000:
            raise Fo1ProfileError(f"MAP object {object_id} has invalid inventory count {inventory_length}")
        prototype = resolver.prototype(pid)
        instance_values = []
        if prototype.object_type == 1:
            extra_count = _instance_extra_count(version, prototype)
            if current_offset + extra_count * 4 > len(data):
                raise Fo1ProfileError("truncated critter MAP instance")
            instance_values = list(struct.unpack_from(f">{extra_count}i", data, current_offset))
            current_offset += extra_count * 4
            instance_flags = instance_values[0]
        else:
            instance_flags, current_offset = _read_i32(data, current_offset, "object instance flags")
            extra_count = _instance_extra_count(version, prototype)
            if current_offset + extra_count * 4 > len(data):
                raise Fo1ProfileError("truncated MAP subtype instance")
            if extra_count:
                instance_values = list(struct.unpack_from(f">{extra_count}i", data, current_offset))
                current_offset += extra_count * 4

        inventory = []
        for inventory_index in range(inventory_length):
            quantity, current_offset = _read_i32(data, current_offset, "inventory quantity")
            nested, current_offset = read_object(current_offset, containing_elevation, depth + 1)
            inventory.append({"index": inventory_index, "quantity": quantity, "object": nested})

        serial += 1
        return (
            {
                "serial": serial,
                "id": object_id,
                "tile": tile,
                "tileX": None if tile < 0 else tile % 200,
                "tileY": None if tile < 0 else tile // 200,
                "pixelOffset": [pixel_x, pixel_y],
                "cachedScreen": [screen_x, screen_y],
                "frame": frame,
                "rotation": rotation,
                "fid": f"{fid:08x}",
                "artFilename": resolver.art_filename(fid),
                "flags": f"{flags:08x}",
                "elevation": stored_elevation,
                "pid": f"{pid:08x}",
                "prototype": {
                    **asdict(prototype),
                    "pid": f"{prototype.pid:08x}",
                    "fid": None if prototype.fid is None else f"{prototype.fid:08x}",
                },
                "combatId": combat_id,
                "lightDistance": light_distance,
                "lightIntensity": light_intensity,
                "outline": outline,
                "sid": f"{sid:08x}",
                "scriptIndex": script_index,
                "inventoryCapacity": inventory_capacity,
                "inventoryPointer": inventory_pointer,
                "instanceFlags": f"{instance_flags & 0xFFFFFFFF:08x}",
                "instanceValues": instance_values,
                "inventory": inventory,
            },
            current_offset,
        )

    elevation_rows = []
    top_level_objects = []
    for elevation in range(3):
        elevation_count, offset = _read_i32(data, offset, f"elevation {elevation} object count")
        if not 0 <= elevation_count <= total_count:
            raise Fo1ProfileError(f"invalid elevation {elevation} object count {elevation_count}")
        objects = []
        for _ in range(elevation_count):
            obj, offset = read_object(offset, elevation, 0)
            objects.append(obj)
            top_level_objects.append(obj)
        elevation_rows.append({"elevation": elevation, "count": elevation_count, "objects": objects})
    if len(top_level_objects) != total_count:
        raise Fo1ProfileError(
            f"MAP top-level object count mismatch: expected {total_count}, got {len(top_level_objects)}"
        )
    return {"totalTopLevelObjects": total_count, "elevations": elevation_rows}, offset


def build_contract(map_path: Path, ettu_root: Path, master_dat: Path) -> dict[str, Any]:
    map_path = map_path.resolve()
    master_dat = master_dat.resolve()
    data = map_path.read_bytes()
    layout = parse_map_layout(data)
    resolver = Fo1ResourceResolver(ettu_root.resolve(), master_dat)
    scripts, object_offset = parse_script_section(data, layout.next_offset)
    objects, end_offset = parse_map_objects(data, object_offset, layout.header.version, resolver)
    if end_offset != len(data):
        raise Fo1ProfileError(f"MAP object graph leaves {len(data) - end_offset} trailing bytes")
    top_level = [obj for elevation in objects["elevations"] for obj in elevation["objects"]]
    doors = [obj for obj in top_level if obj["prototype"]["subtype_name"] == "door"]
    return {
        "schema": CONTRACT_SCHEMA,
        "status": "transported-object-graph",
        "source": {
            "map": {"file": map_path.name, "bytes": len(data), "sha256": sha256_path(map_path)},
            "fallout2Master": {
                "file": master_dat.name,
                "bytes": master_dat.stat().st_size,
                "sha256": sha256_path(master_dat),
            },
        },
        "map": {
            "header": asdict(layout.header),
            "scriptsOffset": layout.next_offset,
            "objectsOffset": object_offset,
            "endOffset": end_offset,
            "scriptLists": scripts,
            "objects": objects,
            "doors": doors,
        },
        "resources": [
            {
                "logicalPath": resource.logical_path,
                "source": resource.source,
                "sha256": resource.sha256,
                "bytes": len(resource.data),
            }
            for resource in sorted(resolver.resources.values(), key=lambda item: item.logical_path)
        ],
        "promotion": {
            "state": "transported",
            "rendered": False,
            "interactive": False,
            "parityReviewed": False,
            "headsetAccepted": False,
        },
        "unsupported": [
            "3D substitution mapping",
            "Godot entity generation",
            "script bytecode execution",
            "turn/AP/RNG simulation",
            "actor assembly and animation",
            "retail parity or package promotion",
        ],
    }


def write_contract(output: Path, contract: dict[str, Any]) -> str:
    sidecar = output.with_suffix(output.suffix + ".sha256")
    if output.exists() or sidecar.exists():
        raise Fo1ProfileError(f"refusing to overwrite MAP object proof: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(contract, indent=2, sort_keys=True) + "\n").encode("utf-8")
    digest = hashlib.sha256(payload).hexdigest()
    with tempfile.NamedTemporaryFile(dir=output.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(payload)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(output)
    sidecar.write_text(f"{digest}  {output.name}\n", encoding="ascii")
    return digest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--map", type=Path, required=True)
    parser.add_argument("--ettu-root", type=Path, required=True)
    parser.add_argument("--fallout2-master", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    contract = build_contract(args.map, args.ettu_root, args.fallout2_master)
    digest = write_contract(args.output.resolve(), contract)
    print(
        json.dumps(
            {
                "schema": contract["schema"],
                "output": str(args.output.resolve()),
                "outputSha256": digest,
                "objects": contract["map"]["objects"]["totalTopLevelObjects"],
                "doors": len(contract["map"]["doors"]),
                "resources": len(contract["resources"]),
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
