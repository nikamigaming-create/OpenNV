#!/usr/bin/env python3
"""Join per-row actor evidence into an exhaustive whole-game coverage ledger."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from actor_parity_corpus import atomic_bytes, atomic_json, jsonl_bytes, output_descriptor
from actor_review_differential import (
    ACTOR_REVIEW_CONTRACT_SCHEMA,
    DIFFERENTIAL_SCHEMA,
    artifact,
    load_json,
    require_object,
    require_text,
    validate_descriptor,
)
from plugin_stack import file_sha256


CORPUS_SCHEMA = "opennv-actor-parity-corpus/v1"
CORPUS_STATUS = "inventory-complete-review-pending"
COVERAGE_SCHEMA = "opennv-actor-review-coverage/v1"
DIFFERENTIAL_REPORT_FILE_NAME = "actor-review-differential-report.json"
APPEARANCE_LEDGER_FILE_NAME = "appearance-coverage.jsonl"
PLACEMENT_LEDGER_FILE_NAME = "placement-coverage.jsonl"
MANIFEST_FILE_NAME = "manifest.json"
PASS_STATUS = "pass"
FAIL_STATUS = "fail"
PENDING_STATUS = "pending"
MISSING_EVIDENCE_STATUS = "missing-evidence"
SUPPORTED_RECORD_TYPES = frozenset({"NPC_", "CREA"})
EXIT_DATA_ERROR = 2


def load_jsonl(path: Path) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for line_number, line in enumerate(
        path.read_text(encoding="utf-8").splitlines(), start=1
    ):
        document = json.loads(line)
        if not isinstance(document, dict):
            raise ValueError(f"Expected an object at {path}:{line_number}")
        rows.append(document)
    return rows


def validate_corpus_output(
    corpus_root: Path,
    descriptor_value: object,
    label: str,
) -> tuple[Path, list[dict[str, object]]]:
    descriptor = require_object(descriptor_value, f"corpus {label} descriptor")
    path = corpus_root / require_text(descriptor.get("file"), f"corpus {label} file")
    if (
        not path.is_file()
        or path.stat().st_size != descriptor.get("bytes")
        or file_sha256(path) != descriptor.get("sha256")
    ):
        raise ValueError(f"Corpus {label} output is missing or changed: {path}")
    rows = load_jsonl(path)
    if len(rows) != descriptor.get("rows"):
        raise ValueError(f"Corpus {label} row count changed: {path}")
    return path, rows


def discover_reports(roots: list[Path]) -> list[Path]:
    discovered: dict[str, Path] = {}
    for root in roots:
        candidates = (
            [root]
            if root.is_file() and root.name == DIFFERENTIAL_REPORT_FILE_NAME
            else list(root.rglob(DIFFERENTIAL_REPORT_FILE_NAME))
            if root.is_dir()
            else []
        )
        if not candidates:
            raise ValueError(f"Differential evidence root contains no reports: {root}")
        for candidate in candidates:
            resolved = candidate.resolve()
            discovered[str(resolved).casefold()] = resolved
    return sorted(discovered.values(), key=lambda path: str(path).casefold())


def validate_differential_report(
    report_path: Path,
    corpus_manifest_path: Path,
    corpus_manifest_sha256: str,
) -> dict[str, object]:
    report = load_json(report_path)
    if (
        report.get("schema") != DIFFERENTIAL_SCHEMA
        or report.get("status") not in {FAIL_STATUS, "human-review-pending"}
        or report.get("parityPassed") is not False
    ):
        raise ValueError(f"Unexpected actor differential report: {report_path}")
    for key in ("scene", "retailContract", "retailReport", "godotReport"):
        validate_descriptor(report.get(key), f"differential {key}")
    contract_path = validate_descriptor(
        report.get("retailContract"), "differential retail contract"
    )
    contract = load_json(contract_path)
    if contract.get("schema") != ACTOR_REVIEW_CONTRACT_SCHEMA:
        raise ValueError(f"Unexpected differential retail contract: {contract_path}")
    provenance = require_object(contract.get("provenance"), "contract provenance")
    source_manifest = require_object(
        provenance.get("corpusManifest"), "contract corpus manifest"
    )
    source_path = Path(
        require_text(source_manifest.get("path"), "contract corpus-manifest path")
    )
    if (
        str(source_path.resolve()).casefold()
        != str(corpus_manifest_path.resolve()).casefold()
        or str(source_manifest.get("sha256", "")).lower()
        != corpus_manifest_sha256
    ):
        raise ValueError(f"Differential belongs to another actor corpus: {report_path}")
    comparisons = report.get("comparisons")
    if not isinstance(comparisons, list) or len(comparisons) != report.get(
        "comparisonCount"
    ):
        raise ValueError(f"Differential comparison count changed: {report_path}")
    ledger = require_object(report.get("coverageLedgerRow"), "differential ledger row")
    if (
        ledger.get("reviewKey") != report.get("reviewKey")
        or ledger.get("recordType") != report.get("recordType")
        or ledger.get("lookedAt") is not False
        or ledger.get("humanReviewStatus") != PENDING_STATUS
        or ledger.get("parityStatus") != FAIL_STATUS
    ):
        raise ValueError(f"Differential ledger row is not fail-closed: {report_path}")
    report["_path"] = str(report_path.resolve())
    report["_sha256"] = file_sha256(report_path)
    return report


def appearance_coverage_rows(
    source_rows: list[dict[str, object]],
    reports_by_review: dict[str, dict[str, object]],
) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    source_keys: set[str] = set()
    for source in source_rows:
        review_key = require_text(source.get("reviewKey"), "appearance review key")
        if review_key in source_keys:
            raise ValueError(f"Duplicate source appearance review key: {review_key}")
        source_keys.add(review_key)
        record_type = require_text(source.get("recordType"), "appearance record type")
        if record_type not in SUPPORTED_RECORD_TYPES:
            raise ValueError(f"Unsupported appearance record type: {record_type}")
        report = reports_by_review.get(review_key)
        if report is None:
            evidence = None
            retail_status = PENDING_STATUS
            godot_status = PENDING_STATUS
            comparison_status = PENDING_STATUS
            human_status = PENDING_STATUS
            looked_at = False
            parity_status = FAIL_STATUS
            status = MISSING_EVIDENCE_STATUS
            compared_samples = 0
        else:
            ledger = require_object(report.get("coverageLedgerRow"), "coverage ledger row")
            if record_type != report.get("recordType"):
                raise ValueError(f"Differential record type differs from corpus: {review_key}")
            evidence = {
                "path": report["_path"],
                "sha256": report["_sha256"],
            }
            retail_status = ledger.get("retailEvidenceStatus")
            godot_status = ledger.get("godotCaptureStatus")
            comparison_status = ledger.get("matchedComparisonStatus")
            human_status = ledger.get("humanReviewStatus")
            looked_at = ledger.get("lookedAt") is True
            parity_status = ledger.get("parityStatus")
            status = parity_status
            compared_samples = report.get("comparisonCount")
        result.append(
            {
                "reviewKey": review_key,
                "baseFormKey": source.get("baseFormKey"),
                "baseRuntimeFormId": source.get("baseRuntimeFormId"),
                "recordType": record_type,
                "editorId": source.get("editorId", ""),
                "requiredShots": source.get("requiredShots"),
                "status": status,
                "retailEvidenceStatus": retail_status,
                "godotCaptureStatus": godot_status,
                "matchedComparisonStatus": comparison_status,
                "humanReviewStatus": human_status,
                "lookedAt": looked_at,
                "parityStatus": parity_status,
                "comparedSamples": compared_samples,
                "evidence": evidence,
            }
        )
    unknown = set(reports_by_review) - source_keys
    if unknown:
        raise ValueError(f"Differential reports reference unknown review keys: {sorted(unknown)}")
    return result


def placement_coverage_rows(
    source_rows: list[dict[str, object]],
) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    keys: set[str] = set()
    for source in source_rows:
        placement_key = require_text(
            source.get("placementFormKey"), "placement review key"
        )
        if placement_key in keys:
            raise ValueError(f"Duplicate placement review key: {placement_key}")
        keys.add(placement_key)
        result.append(
            {
                "placementFormKey": placement_key,
                "placementRuntimeFormId": source.get("placementRuntimeFormId"),
                "recordType": source.get("recordType"),
                "cell": source.get("cell"),
                "candidateBaseFormKeys": source.get("candidateBaseFormKeys"),
                "requiredShots": source.get("requiredShots"),
                "status": MISSING_EVIDENCE_STATUS,
                "retailEvidenceStatus": PENDING_STATUS,
                "godotCaptureStatus": PENDING_STATUS,
                "matchedComparisonStatus": PENDING_STATUS,
                "humanReviewStatus": PENDING_STATUS,
                "lookedAt": False,
                "parityStatus": FAIL_STATUS,
                "evidence": None,
            }
        )
    return result


def appearance_counts(rows: list[dict[str, object]]) -> dict[str, object]:
    by_type: dict[str, dict[str, int]] = {}
    for record_type in sorted(SUPPORTED_RECORD_TYPES):
        typed = [row for row in rows if row["recordType"] == record_type]
        by_type[record_type] = {
            "total": len(typed),
            "evidenceReports": sum(row["evidence"] is not None for row in typed),
            "lookedAt": sum(row["lookedAt"] is True for row in typed),
            "parityPassed": sum(row["parityStatus"] == PASS_STATUS for row in typed),
        }
    return {
        "total": len(rows),
        "evidenceReports": sum(row["evidence"] is not None for row in rows),
        "missingEvidence": sum(row["status"] == MISSING_EVIDENCE_STATUS for row in rows),
        "objectiveFailed": sum(
            row["matchedComparisonStatus"] == FAIL_STATUS for row in rows
        ),
        "humanReviewed": sum(row["lookedAt"] is True for row in rows),
        "parityPassed": sum(row["parityStatus"] == PASS_STATUS for row in rows),
        "byRecordType": by_type,
    }


def build_actor_review_coverage(
    corpus_root: Path,
    differential_roots: list[Path],
    output_root: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite actor coverage ledger: {output_root}")
    manifest_path = corpus_root / MANIFEST_FILE_NAME
    manifest = load_json(manifest_path)
    if (
        manifest.get("schema") != CORPUS_SCHEMA
        or manifest.get("status") != CORPUS_STATUS
    ):
        raise ValueError(f"Unexpected actor parity corpus: {manifest_path}")
    outputs = require_object(manifest.get("outputs"), "corpus outputs")
    _, appearance_sources = validate_corpus_output(
        corpus_root, outputs.get("appearanceReview"), "appearance review"
    )
    _, placement_sources = validate_corpus_output(
        corpus_root, outputs.get("placementReview"), "placement review"
    )
    corpus_sha256 = file_sha256(manifest_path)
    report_paths = discover_reports(differential_roots)
    reports = [
        validate_differential_report(path, manifest_path, corpus_sha256)
        for path in report_paths
    ]
    reports_by_review: dict[str, dict[str, object]] = {}
    for report in reports:
        review_key = require_text(report.get("reviewKey"), "differential review key")
        if review_key in reports_by_review:
            raise ValueError(f"Duplicate differential report for review: {review_key}")
        reports_by_review[review_key] = report

    appearances = appearance_coverage_rows(appearance_sources, reports_by_review)
    placements = placement_coverage_rows(placement_sources)
    appearance_summary = appearance_counts(appearances)
    placement_summary = {
        "total": len(placements),
        "evidenceReports": 0,
        "missingEvidence": len(placements),
        "humanReviewed": 0,
        "parityPassed": 0,
    }
    whole_game_pass = (
        appearance_summary["parityPassed"] == appearance_summary["total"]
        and appearance_summary["humanReviewed"] == appearance_summary["total"]
        and placement_summary["parityPassed"] == placement_summary["total"]
        and placement_summary["humanReviewed"] == placement_summary["total"]
    )

    output_root.mkdir(parents=True)
    appearance_path = output_root / APPEARANCE_LEDGER_FILE_NAME
    placement_path = output_root / PLACEMENT_LEDGER_FILE_NAME
    atomic_bytes(appearance_path, jsonl_bytes(appearances))
    atomic_bytes(placement_path, jsonl_bytes(placements))
    result = {
        "schema": COVERAGE_SCHEMA,
        "status": PASS_STATUS if whole_game_pass else FAIL_STATUS,
        "wholeGameParityPassed": whole_game_pass,
        "corpus": artifact(manifest_path),
        "differentialReports": [artifact(path) for path in report_paths],
        "counts": {
            "appearance": appearance_summary,
            "placement": placement_summary,
        },
        "outputs": {
            "appearance": output_descriptor(appearance_path, len(appearances)),
            "placement": output_descriptor(placement_path, len(placements)),
        },
        "evidencePolicy": {
            "everyCorpusAppearanceRequired": True,
            "everyCorpusPlacementRequired": True,
            "missingEvidenceCannotPass": True,
            "failedComparisonCannotPass": True,
            "humanReviewRequired": True,
            "samplingCannotEstablishWholeGameParity": True,
        },
    }
    atomic_json(output_root / MANIFEST_FILE_NAME, result)
    result["manifest"] = str((output_root / MANIFEST_FILE_NAME).resolve())
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument(
        "--differential-root", type=Path, required=True, action="append"
    )
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = build_actor_review_coverage(
            args.corpus_root.resolve(),
            [path.resolve() for path in args.differential_root],
            args.output_root.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_ACTOR_REVIEW_COVERAGE_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_ACTOR_REVIEW_COVERAGE "
        + json.dumps(
            {
                "manifest": result["manifest"],
                "status": result["status"],
                "appearance": result["counts"]["appearance"],
                "placement": result["counts"]["placement"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
