"""No-media, fail-closed walk-mask proof for an explicit FO1 exit-grid descriptor."""

from __future__ import annotations

import argparse
import hashlib
import json
from collections import deque
from pathlib import Path


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def neighbors(tile: int, width: int, height: int):
    x, y = tile % width, tile // width
    # The cache owns the offset layout; this is only its named even-column adjacency relation.
    columns = ((-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (0, -1)) if x % 2 == 0 else ((-1, -1), (-1, 0), (0, 1), (1, 0), (1, -1), (0, -1))
    for dx, dy in columns:
        nx, ny = x + dx, y + dy
        if 0 <= nx < width and 0 <= ny < height:
            yield ny * width + nx


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--hex-scene", type=Path, required=True)
    parser.add_argument("--exit-grid-transition", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise SystemExit(f"refusing to overwrite exit-grid proof: {args.output}")
    scene, contract = json.loads(args.hex_scene.read_text(encoding="utf-8")), json.loads(args.exit_grid_transition.read_text(encoding="utf-8"))
    if scene.get("schema") != "opennv-fo1-hex-scene/v1" or contract.get("schema") != "opennv-fo1-exit-grid-transition/v1":
        raise SystemExit("unexpected explicit source contract")
    if scene["source"]["map"]["sha256"] != contract["sourceMap"]["sha256"]:
        raise SystemExit("descriptor does not belong to supplied scene")
    grid, entry = scene["grid"], scene["entry"]["tile"]
    width, height = grid["hexWidth"], grid["hexHeight"]
    blocked, floor_ids, default = set(grid["blockedHexes"]), grid["floorIds"], grid["defaultFloorId"]
    floor_width = grid["floorWidth"]
    def walkable(tile: int) -> bool:
        return floor_ids[(tile // width // 2) * floor_width + (floor_width - 1 - (tile % width) // 2)] != default and tile not in blocked
    goals = {row["tile"] for row in contract["triggers"]}
    visited, queue = {entry}, deque([entry])
    while queue:
        tile = queue.popleft()
        for neighbor in neighbors(tile, width, height):
            if walkable(neighbor) and neighbor not in visited:
                visited.add(neighbor)
                queue.append(neighbor)
    reachable = sorted(goals & visited)
    document = {
        "schema": "opennv-fo1-exit-grid-transition-headless-proof/v1",
        "status": "pass-source-walk-mask-transition-ready" if reachable else "blocked-source-walk-mask-door-transition-unimplemented",
        "rendered": False,
        "interactive": False,
        "scene": {"path": str(args.hex_scene.resolve()), "sha256": digest(args.hex_scene)},
        "exitGridTransition": {"path": str(args.exit_grid_transition.resolve()), "sha256": digest(args.exit_grid_transition), "source": contract["sourceMap"], "destination": contract["destination"], "triggerTiles": sorted(goals)},
        "movement": {"entryTile": entry, "sourceWalkMaskOnly": True, "reachableTriggerTiles": reachable, "componentTileCount": len(visited)},
        "destinationSceneLoaded": False,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(document["status"])
    return 0 if reachable else 2


if __name__ == "__main__":
    raise SystemExit(main())
