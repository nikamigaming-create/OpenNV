"""Load hash-bound actor presentation state from gallery retail evidence.

The gallery compiler uses this module to retain the active KF stack and any
visible equipped attachment published by the retail actor.  No subject names,
animation names, equipment FormIDs, or attachment slots are selected here.
"""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path, PureWindowsPath

from actor_review_contract import (
    RETAIL_APPEARANCE_SCHEMA,
    WEAPON_RENDER_STATE_NOT_APPLICABLE,
    WEAPON_RENDER_STATE_NOT_VISIBLE_AT_FRAME,
    WEAPON_RENDER_STATE_VISIBLE_SOURCE_BOUND,
    WEAPON_STATE_EQUIPPED,
    WEAPON_STATE_NONE,
    _event_hash,
)
from bsa_archive import canonical_member_path
from plugin_stack import file_sha256


EVIDENCE_SCHEMA = "opennv-gallery-retail-evidence/v4"
EVIDENCE_STATUS = "retail-authored-reference-observed"
NIF_SUFFIX = ".nif"
KF_SUFFIX = ".kf"


@dataclass(frozen=True)
class RetailAnimationSequence:
    logical_path: str
    state: int
    cycle: int
    weight: float
    frequency: float
    phase_seconds: float
    group: int


@dataclass(frozen=True)
class RetailVisibleAttachment:
    role: str
    source_form_id: str
    source_slot: int
    model_path: str


@dataclass(frozen=True)
class GalleryActorPresentation:
    evidence_path: Path
    evidence_sha256: str
    oracle_path: Path
    oracle_sha256: str
    presentation_frame: int
    actor_snapshot_event_sha256: str
    actor_pose_event_sha256: str
    appearance_frame: int
    appearance_event_sha256: str
    presentation_surface_report_path: Path
    presentation_surface_report_sha256: str
    presentation_surface_geometry_names: tuple[str, ...]
    appearance: dict[str, object]
    weapon_form: int
    weapon_out: bool
    animations: tuple[RetailAnimationSequence, ...]
    visible_attachments: tuple[RetailVisibleAttachment, ...]
    visible_weapon: RetailVisibleAttachment | None


def _verified_descriptor(
    descriptor: object,
    label: str,
) -> tuple[Path, str]:
    if not isinstance(descriptor, dict):
        raise ValueError(f"Gallery {label} descriptor is not an object")
    path = Path(str(descriptor.get("path", ""))).resolve()
    if not path.is_file():
        raise FileNotFoundError(f"Gallery {label} is missing: {path}")
    sha256 = file_sha256(path).lower()
    if (
        path.stat().st_size != int(descriptor.get("bytes", -1))
        or sha256 != str(descriptor.get("sha256", "")).lower()
    ):
        raise ValueError(f"Gallery {label} differs from its descriptor: {path}")
    return path, sha256


def _appearance_snapshot(oracle_path: Path) -> dict[str, object]:
    matches: list[dict[str, object]] = []
    with oracle_path.open("r", encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, start=1):
            try:
                event = json.loads(line)
            except json.JSONDecodeError as error:
                raise ValueError(
                    f"Retail oracle has invalid JSON at line {line_number}: {oracle_path}"
                ) from error
            if (
                isinstance(event, dict)
                and event.get("event") == "actor-visual-snapshot"
                and isinstance(event.get("appearance"), dict)
            ):
                matches.append(event)
    if len(matches) != 1:
        raise ValueError(
            "Gallery retail oracle must contain exactly one actor appearance snapshot: "
            f"{oracle_path}"
        )
    return matches[0]


def _presentation_surface_geometry_names(
    report_path: Path,
    presentation_frame: int,
    shot_kind: str,
    required_status: str,
) -> tuple[str, ...]:
    report = json.loads(report_path.read_text(encoding="utf-8"))
    source_frames = (
        report.get("runtime", {})
        .get("surfaceContract", {})
        .get("sourceFrames")
    )
    if not isinstance(source_frames, list):
        raise ValueError("Gallery retail report has no surface source-frame ledger")
    matches = [
        row
        for row in source_frames
        if isinstance(row, dict)
        and int(row.get("sourceFrame", -1)) == presentation_frame
        and str(row.get("shotKind", "")) == shot_kind
        and str(row.get("status", "")) == required_status
    ]
    if len(matches) != 1:
        raise ValueError(
            "Gallery retail presentation has no unique selected-frame surface contract"
        )
    surfaces = matches[0].get("surfaces")
    if not isinstance(surfaces, list) or not surfaces:
        raise ValueError("Gallery retail presentation surface contract is empty")
    names = {
        str(name)
        for surface in surfaces
        if isinstance(surface, dict)
        for name in surface.get("geometryNames", [])
        if str(name)
    }
    if not names:
        raise ValueError("Gallery retail presentation has no visible geometry names")
    return tuple(sorted(names))


def _creature_appearance_at_presentation(
    appearance: dict[str, object],
    visible_geometry_names: tuple[str, ...],
) -> dict[str, object]:
    render_parts = appearance.get("renderParts")
    if not isinstance(render_parts, list):
        raise ValueError("Gallery retail creature appearance has no render parts")
    visible = set(visible_geometry_names)
    known = {
        str(part.get("geometryName", ""))
        for part in render_parts
        if isinstance(part, dict)
    }
    missing = sorted(visible - known)
    if missing:
        raise ValueError(
            "Gallery retail selected-frame geometry is absent from the hash-bound "
            f"appearance snapshot: {missing}"
        )
    selected_parts = []
    for part in render_parts:
        if not isinstance(part, dict):
            raise ValueError("Gallery retail creature render part is not an object")
        row = dict(part)
        row["visible"] = str(row.get("geometryName", "")) in visible
        selected_parts.append(row)
    result = dict(appearance)
    result["renderParts"] = selected_parts
    return result


def _animation_sequences(source: object) -> tuple[RetailAnimationSequence, ...]:
    if not isinstance(source, list) or not source:
        raise ValueError("Gallery retail presentation has no active animation stack")
    result: list[RetailAnimationSequence] = []
    identities: set[str] = set()
    for row in source:
        if not isinstance(row, dict):
            raise ValueError("Gallery retail animation sequence is not an object")
        logical_path = canonical_member_path(str(row.get("file", "")))
        if PureWindowsPath(logical_path).suffix.lower() != KF_SUFFIX:
            raise ValueError(
                f"Gallery retail animation is not an owned KF member: {logical_path}"
            )
        identity = logical_path.casefold()
        if identity in identities:
            raise ValueError(
                f"Gallery retail animation stack repeats {logical_path}"
            )
        identities.add(identity)
        weight = float(row.get("weight", float("nan")))
        frequency = float(row.get("frequency", float("nan")))
        phase_seconds = float(row.get("lastScaledSeconds", float("nan")))
        if (
            not math.isfinite(weight)
            or weight <= 0.0
            or not math.isfinite(frequency)
            or frequency <= 0.0
            or not math.isfinite(phase_seconds)
            or phase_seconds < 0.0
        ):
            raise ValueError(
                f"Gallery retail animation state is invalid: {logical_path}"
            )
        result.append(
            RetailAnimationSequence(
                logical_path,
                int(row["state"]),
                int(row["cycle"]),
                weight,
                frequency,
                phase_seconds,
                int(row["group"]),
            )
        )
    return tuple(result)


def _visible_weapon(
    appearance: dict[str, object],
    presentation_weapon_form: int,
    presentation_weapon_out: bool,
) -> RetailVisibleAttachment | None:
    weapon = appearance.get("equippedWeapon")
    render_parts = appearance.get("renderParts")
    if not isinstance(weapon, dict) or not isinstance(render_parts, list):
        raise ValueError("Gallery retail appearance has no equipment/render-part contract")
    state = str(weapon.get("state", ""))
    render_state = str(weapon.get("renderState", ""))
    source_form_id = str(weapon.get("sourceFormId", ""))
    try:
        source_form = int(source_form_id, 16)
    except ValueError as error:
        raise ValueError(
            f"Gallery retail weapon FormID is invalid: {source_form_id}"
        ) from error
    if source_form != presentation_weapon_form:
        raise ValueError(
            "Gallery retail appearance and presentation weapon FormID differ"
        )
    visible_parts = [
        part
        for part in render_parts
        if isinstance(part, dict)
        and part.get("role") == "weapon"
        and bool(part.get("required"))
        and bool(part.get("attached"))
        and bool(part.get("drawable"))
        and bool(part.get("visible"))
    ]
    if state == WEAPON_STATE_NONE:
        if (
            render_state != WEAPON_RENDER_STATE_NOT_APPLICABLE
            or source_form != 0
            or visible_parts
        ):
            raise ValueError("Gallery retail no-weapon state is inconsistent")
        return None
    if state != WEAPON_STATE_EQUIPPED or source_form == 0:
        raise ValueError(f"Gallery retail weapon state is unsupported: {state}")
    if render_state == WEAPON_RENDER_STATE_NOT_VISIBLE_AT_FRAME:
        if visible_parts:
            raise ValueError("Gallery retail hidden weapon has visible render parts")
        return None
    if render_state != WEAPON_RENDER_STATE_VISIBLE_SOURCE_BOUND:
        raise ValueError(
            f"Gallery retail weapon render state is unsupported: {render_state}"
        )
    model_path = canonical_member_path(str(weapon.get("modelPath", "")))
    if PureWindowsPath(model_path).suffix.lower() != NIF_SUFFIX:
        raise ValueError(f"Gallery retail weapon is not a NIF: {model_path}")
    matching = [
        part
        for part in visible_parts
        if str(part.get("sourceFormId", "")) == source_form_id
        and canonical_member_path(str(part.get("modelPath", ""))) == model_path
    ]
    source_slots = {int(part["sourceSlot"]) for part in matching}
    if not matching or len(source_slots) != 1:
        raise ValueError(
            "Gallery retail visible weapon has no unique authored attachment slot"
        )
    return RetailVisibleAttachment(
        "weapon",
        source_form_id,
        source_slots.pop(),
        model_path,
    )


def _visible_model_attachments(
    appearance: dict[str, object],
) -> tuple[RetailVisibleAttachment, ...]:
    render_parts = appearance.get("renderParts")
    if not isinstance(render_parts, list):
        raise ValueError("Gallery retail appearance has no render-part contract")
    identities: set[tuple[str, str, int, str]] = set()
    for part in render_parts:
        if not isinstance(part, dict) or not (
            bool(part.get("required"))
            and bool(part.get("attached"))
            and bool(part.get("drawable"))
            and bool(part.get("visible"))
        ):
            continue
        raw_model_path = str(part.get("modelPath", ""))
        if not raw_model_path:
            continue
        model_path = canonical_member_path(raw_model_path)
        if PureWindowsPath(model_path).suffix.lower() != NIF_SUFFIX:
            raise ValueError(
                f"Gallery retail visible attachment is not a NIF: {model_path}"
            )
        source_form_id = str(part.get("sourceFormId", ""))
        try:
            source_form = int(source_form_id, 16)
        except ValueError as error:
            raise ValueError(
                f"Gallery retail attachment FormID is invalid: {source_form_id}"
            ) from error
        if source_form <= 0:
            raise ValueError("Gallery retail visible attachment has no source FormID")
        source_slot = int(part.get("sourceSlot", -1))
        if source_slot < 0 or source_slot > 0xFFFFFFFF:
            raise ValueError(
                f"Gallery retail attachment has invalid source slot: {source_slot}"
            )
        identities.add(
            (
                str(part.get("role", "")),
                f"0x{source_form:08X}",
                source_slot,
                model_path,
            )
        )
    return tuple(
        RetailVisibleAttachment(*identity)
        for identity in sorted(identities)
    )


def load_gallery_actor_presentation(
    descriptor: object,
    expected_reference_form_id: str,
    expected_base_form_id: str,
) -> GalleryActorPresentation:
    evidence_path, evidence_sha256 = _verified_descriptor(
        descriptor,
        "retail evidence",
    )
    evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
    if (
        not isinstance(evidence, dict)
        or evidence.get("schema") != EVIDENCE_SCHEMA
        or evidence.get("status") != EVIDENCE_STATUS
    ):
        raise ValueError(f"Unexpected gallery retail evidence: {evidence_path}")
    shot = evidence.get("shot")
    retail = evidence.get("retail")
    if not isinstance(shot, dict) or not isinstance(retail, dict):
        raise ValueError("Gallery retail evidence has no shot/retail contract")
    if (
        int(str(shot.get("referenceFormId", "0")), 16)
        != int(expected_reference_form_id, 16)
        or int(str(shot.get("baseFormId", "0")), 16)
        != int(expected_base_form_id, 16)
    ):
        raise ValueError("Gallery retail evidence identifies another actor")
    oracle_path, oracle_sha256 = _verified_descriptor(
        retail.get("oracleJsonl"),
        "retail oracle",
    )
    report_path, report_sha256 = _verified_descriptor(
        retail.get("report"),
        "retail report",
    )
    presentation = retail.get("presentation")
    if not isinstance(presentation, dict) or not isinstance(
        presentation.get("actor"), dict
    ):
        raise ValueError("Gallery retail evidence has no actor presentation")
    actor = presentation["actor"]
    weapon_form = int(actor.get("weaponForm", 0))
    weapon_out = bool(actor.get("weaponOut"))
    animations = _animation_sequences(actor.get("animationDataSequences"))
    snapshot = _appearance_snapshot(oracle_path)
    appearance = snapshot["appearance"]
    if (
        appearance.get("schema") != RETAIL_APPEARANCE_SCHEMA
        or not bool(appearance.get("complete"))
        or bool(appearance.get("truncated"))
    ):
        raise ValueError("Gallery retail actor appearance is incomplete")
    selection = presentation.get("selection")
    if not isinstance(selection, dict):
        raise ValueError("Gallery retail presentation has no selection proof")
    presentation_surface_geometry_names = _presentation_surface_geometry_names(
        report_path,
        int(presentation["frame"]),
        str(presentation["shotKind"]),
        str(selection["surfaceStatus"]),
    )
    if str(shot.get("recordType", "")) == "CREA":
        appearance = _creature_appearance_at_presentation(
            appearance,
            presentation_surface_geometry_names,
        )
    visible_attachments = _visible_model_attachments(appearance)
    return GalleryActorPresentation(
        evidence_path,
        evidence_sha256,
        oracle_path,
        oracle_sha256,
        int(presentation["frame"]),
        str(presentation["actorSnapshotEventSha256"]),
        str(presentation["actorPoseEventSha256"]),
        int(snapshot["frame"]),
        _event_hash(snapshot),
        report_path,
        report_sha256,
        presentation_surface_geometry_names,
        appearance,
        weapon_form,
        weapon_out,
        animations,
        visible_attachments,
        _visible_weapon(appearance, weapon_form, weapon_out),
    )
