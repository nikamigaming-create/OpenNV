#!/usr/bin/env python3
"""Compile immutable pre-evidence retail capture requests from one gallery recipe."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

from prepare_wasteland_gallery import _gallery_shot_identity, _load_gallery
from runtime_configuration import configuration_path, load_runtime_configuration


CAPTURE_SHOT_SCHEMA = "opennv-gallery-capture-shot/v1"
CAPTURE_SHOT_STATUS = "owned-authored-capture-request"
CAPTURE_MANIFEST_SCHEMA = "opennv-gallery-capture-manifest/v1"
CAPTURE_MANIFEST_STATUS = "complete-owned-authored-capture-requests"
EXIT_DATA_ERROR = 2


def _sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _descriptor(path: Path) -> dict[str, object]:
    resolved = path.resolve()
    return {
        "path": str(resolved),
        "bytes": resolved.stat().st_size,
        "sha256": _sha256(resolved),
    }


def _atomic_json(path: Path, document: object) -> None:
    if path.exists():
        raise FileExistsError(f"Refusing to overwrite capture contract: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    os.replace(temporary, path)


def _capture_shot_contract(
    subject: dict[str, object],
    subject_profile: dict[str, object],
    location: dict[str, object],
    gallery_descriptor: dict[str, object],
    runtime_configuration_descriptor: dict[str, object],
) -> dict[str, object]:
    return {
        "schema": CAPTURE_SHOT_SCHEMA,
        "status": CAPTURE_SHOT_STATUS,
        "gallery": gallery_descriptor.copy(),
        "runtimeConfiguration": runtime_configuration_descriptor.copy(),
        **_gallery_shot_identity(subject, subject_profile, location),
    }


def prepare_capture_shots(
    gallery_path: Path,
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(
            f"Refusing to overwrite gallery capture contracts: {output_root}"
        )
    gallery = _load_gallery(gallery_path)
    configuration = load_runtime_configuration()
    gallery_descriptor = _descriptor(gallery_path)
    configuration_descriptor = _descriptor(configuration_path())
    locations = {str(row["id"]): row for row in gallery["locations"]}
    if len(locations) != len(gallery["locations"]):
        raise ValueError("Gallery location IDs are not unique")

    contracts_root = output_root / "shots"
    rows = []
    for subject in gallery["subjects"]:
        location = locations.get(str(subject["locationId"]))
        if location is None:
            raise ValueError(f"Gallery subject has no location: {subject['id']}")
        profile = gallery["subjectProfiles"][str(subject["profile"])]
        contract = _capture_shot_contract(
            subject,
            profile,
            location,
            gallery_descriptor,
            configuration_descriptor,
        )
        path = contracts_root / f"{int(subject['ordinal']):02d}-{subject['id']}.json"
        _atomic_json(path, contract)
        rows.append(
            {
                "ordinal": int(subject["ordinal"]),
                "id": str(subject["id"]),
                **_descriptor(path),
            }
        )

    manifest = {
        "schema": CAPTURE_MANIFEST_SCHEMA,
        "status": CAPTURE_MANIFEST_STATUS,
        "gallery": gallery_descriptor,
        "runtimeConfiguration": configuration_descriptor,
        "shotCount": len(rows),
        "shots": rows,
        "complexity": {
            "locationLookup": "single-pass-hash-index",
            "processingOrder": "gallery-plus-subjects",
        },
    }
    manifest_path = output_root / "gallery-capture-manifest.json"
    _atomic_json(manifest_path, manifest)
    manifest["manifest"] = str(manifest_path.resolve())
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gallery", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = prepare_capture_shots(
            args.gallery.resolve(),
            args.output_root.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_GALLERY_CAPTURE_SHOTS_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_GALLERY_CAPTURE_SHOTS "
        + json.dumps(
            {
                "manifest": result["manifest"],
                "shotCount": result["shotCount"],
                "status": result["status"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
