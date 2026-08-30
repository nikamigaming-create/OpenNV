#!/usr/bin/env python3
"""Compile the explicit source-script use contract for the admitted VAULT13 flare stack."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import tempfile
from pathlib import Path
from typing import Any

from fo1_profile import Fo1ProfileError, sha256_path


SCHEMA = "opennv-fo1-destination-flare-use/v1"
INTERACTION_SCHEMA = "opennv-fo1-destination-inventory-interaction/v1"


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def require_define(payload: str, symbol: str, value: int) -> None:
    match = re.search(rf"^\s*#define\s+{re.escape(symbol)}\s+\(\s*(\d+)\s*\)", payload, re.MULTILINE)
    if match is None or int(match.group(1)) != value:
        raise Fo1ProfileError(f"source header does not bind {symbol} to its expected source value")


def build(interaction_path: Path, item_header: Path, scripts_header: Path,
          flare_script: Path, output: Path) -> dict[str, str]:
    if output.exists():
        raise Fo1ProfileError(f"refusing to overwrite flare use descriptor: {output}")
    interaction = read_json(interaction_path)
    if interaction.get("schema") != INTERACTION_SCHEMA:
        raise Fo1ProfileError("unexpected destination inventory interaction schema")
    items = interaction.get("host", {}).get("items", [])
    flares = [row for row in items if row.get("symbol") == "PID_FLARE"]
    if len(flares) != 1:
        raise Fo1ProfileError("destination inventory interaction does not uniquely admit PID_FLARE")
    flare = flares[0]
    if flare.get("pid") != "0000004f" or flare.get("profile", {}).get("subtypeName") != "weapon":
        raise Fo1ProfileError("destination flare item does not retain its owned PRO identity")
    require_define(item_header.read_text(encoding="cp1252"), "PID_FLARE", int(flare["pid"], 16))
    require_define(scripts_header.read_text(encoding="cp1252"), "SCRIPT_FLARE", 223)
    script = flare_script.read_text(encoding="cp1252")
    if not re.search(r"script_action\s*==\s*use_proc", script, re.IGNORECASE) or \
            not re.search(r"lit\s*:=\s*1", script, re.IGNORECASE) or not re.search(
        r"set_local_var\s*\(\s*0\s*,\s*game_time\s*\)", script, re.IGNORECASE):
        raise Fo1ProfileError("SCRIPT_FLARE use_proc does not provide the bounded lit-state behavior")
    if not re.search(r"game_time\s*-\s*local_var\s*\(\s*0\s*\)", script, re.IGNORECASE):
        raise Fo1ProfileError("SCRIPT_FLARE does not retain an explicit game-time expiry guard")
    document = {
        "schema": SCHEMA,
        "status": "compiled-owned-scripted-flare-use",
        "interaction": {"path": str(interaction_path.resolve()), "sha256": sha256_path(interaction_path)},
        "item": {"hostSerial": interaction["host"]["serial"], "symbol": flare["symbol"], "pid": flare["pid"],
                 "prototypeSha256": flare["prototypeSha256"], "profile": flare["profile"]},
        "script": {"symbol": "SCRIPT_FLARE", "path": str(flare_script.resolve()), "sha256": sha256_path(flare_script)},
        "inputs": {"itemPidHeader": {"path": str(item_header.resolve()), "sha256": sha256_path(item_header)},
                   "scriptsHeader": {"path": str(scripts_header.resolve()), "sha256": sha256_path(scripts_header)}},
        "semantics": {"action": "use_proc", "result": "lit-state", "storesGameTime": True,
                      "expiry": "unimplemented-fail-closed", "activeHand": "not-proven-by-script", "renderedLight": False},
        "rendered": False, "interactive": False, "retailOrDerivedAssetsPackaged": False,
    }
    encoded = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=output.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(encoded); stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary, output)
    return {"path": str(output.resolve()), "sha256": hashlib.sha256(encoded).hexdigest()}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--interaction", type=Path, required=True)
    parser.add_argument("--item-pid-header", type=Path, required=True)
    parser.add_argument("--scripts-header", type=Path, required=True)
    parser.add_argument("--flare-script", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        print("OPENNV_FO1_FLARE_USE " + json.dumps(build(
            args.interaction, args.item_pid_header, args.scripts_header,
            args.flare_script, args.output), sort_keys=True))
        return 0
    except Exception as error:
        print(f"OPENNV_FO1_FLARE_USE_ERROR {error}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
