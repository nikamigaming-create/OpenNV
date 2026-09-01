"""Extract the owned FNV world-wind denominator without inventing force math."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from environment_catalog import (
    WEATHER_WIND_SPEED_DATA_OFFSET,
    EnvironmentCatalog,
    scan_environment_catalog,
)


SCHEMA = "opennv-fnv-world-wind-denominator/v1"
STATUS = "source-denominator-force-equation-unresolved"
MAXIMUM_WIND_SETTING_NAME = "fMaximumWind"
WEATHER_LISTENER_TYPE_NAME = "TESWindListener"
HAVOK_LISTENER_TYPE_NAME = "bhkWindListener"
MAXIMUM_WIND_SETTING = f"{MAXIMUM_WIND_SETTING_NAME}\0".encode("ascii")
WEATHER_LISTENER_TYPE = f".?AV{WEATHER_LISTENER_TYPE_NAME}@@\0".encode("ascii")
HAVOK_LISTENER_TYPE = f".?AV{HAVOK_LISTENER_TYPE_NAME}@@\0".encode("ascii")
REQUIRED_EXECUTABLE_IDENTITIES = (
    MAXIMUM_WIND_SETTING,
    WEATHER_LISTENER_TYPE,
    HAVOK_LISTENER_TYPE,
)


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def extract_world_wind_denominator(
    executable: bytes,
    catalog: EnvironmentCatalog,
) -> dict[str, object]:
    identity_counts = {
        identity.rstrip(b"\x00").decode("ascii"): executable.count(identity)
        for identity in REQUIRED_EXECUTABLE_IDENTITIES
    }
    if any(count != 1 for count in identity_counts.values()):
        raise ValueError(
            "Owned executable does not expose one exact FNV wind identity set"
        )
    weather = [
        {
            "formId": f"0x{form_id:08X}",
            "editorId": value.editor_id,
            "sourceSubrecord": "DATA",
            "sourceByteOffset": WEATHER_WIND_SPEED_DATA_OFFSET,
            "storage": "uint8",
            "magnitudeByte": value.wind_speed_byte,
        }
        for form_id, value in sorted(catalog.weather.items())
    ]
    if not weather:
        raise ValueError("Owned master has no WTHR wind magnitudes")
    return {
        "schema": SCHEMA,
        "status": STATUS,
        "executable": {
            "sha256": _sha256(executable),
            "identityCounts": identity_counts,
        },
        "maximumWind": {
            "setting": MAXIMUM_WIND_SETTING_NAME,
            "value": None,
            "status": "engine-default-value-unresolved",
        },
        "weather": weather,
        "listener": {
            "weatherType": WEATHER_LISTENER_TYPE_NAME,
            "havokType": HAVOK_LISTENER_TYPE_NAME,
            "equationStatus": "unresolved-requires-observe-only-runtime-trace",
        },
        "runtimeDisposition": "do-not-apply-world-force",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--master", required=True, type=Path)
    parser.add_argument("--executable", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = extract_world_wind_denominator(
        args.executable.read_bytes(),
        scan_environment_catalog(args.master),
    )
    payload = json.dumps(result, indent=2, sort_keys=True) + "\n"
    if args.output is None:
        print(payload, end="")
    else:
        args.output.write_text(payload, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
