#!/usr/bin/env python3
"""Validate runtime proof reports against owned-data manifests and shared policy."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from runtime_configuration import RuntimeConfiguration, load_runtime_configuration


CELL_REPORT_SCHEMA = "opennv-godot-cell/v1"
XR_REPORT_SCHEMA = "opennv-openxr-rig/v2"
GAMEPLAY_REPORT_SCHEMA = "opennv-godot-playable-route/v1"
SANDBOX_SAVE_SCHEMA = "opennv-sandbox-save/v2"
POOL_REPORT_SCHEMA = "opennv-pool-practice/v1"
FLOAT_COMPARISON_TOLERANCE = 1.0e-6


def _read(path: Path) -> dict[str, object]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return value


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def _verify_configuration(
    report: dict[str, object],
    configuration: RuntimeConfiguration,
) -> None:
    _require(
        report.get("configurationSchema") == configuration.document["schema"]
        and str(report.get("configurationSha256", "")).lower() == configuration.sha256,
        "Runtime report was produced with another OpenNV configuration",
    )


def validate_xr_report(
    report: dict[str, object],
    configuration: RuntimeConfiguration,
) -> None:
    _verify_configuration(report, configuration)
    xr = configuration.document["xr"]
    contract = xr["contract"]
    loadout = xr["diagnosticRigProof"]
    save = report["sharedSaveSchema"]
    _require(report.get("schema") == XR_REPORT_SCHEMA, "Unexpected OpenXR report schema")
    _require(report.get("status") == "pass", "OpenXR report did not pass")
    _require(not bool(report["viewportXrEnabledDuringProof"]), "Headless XR proof enabled the viewport")
    _require(int(report["actionSets"]) == int(contract["expectedActionSetCount"]), "XR action-set count differs")
    _require(sorted(report["actionNames"]) == sorted(contract["actionNames"]), "XR action names differ")
    _require(
        sorted(report["testedInteractionProfiles"]) == sorted(contract["interactionProfilePaths"]),
        "XR interaction profiles differ",
    )
    _require(report["originType"] == "XROrigin3D", "XR origin type differs")
    _require(report["cameraType"] == "XRCamera3D", "XR camera type differs")
    _require(
        report["controllerRenderModelManagerType"] == "OpenXRRenderModelManager",
        "XR render-model manager differs",
    )
    _require(report["leftTracker"] == "left_hand", "XR left tracker differs")
    _require(report["rightTracker"] == "right_hand", "XR right tracker differs")
    _require(float(report["worldScale"]) == float(xr["worldScale"]), "XR world scale differs")
    _require(
        float(report["desiredEyeHeightMeters"]) == float(xr["desiredEyeHeightMeters"]),
        "XR desired eye height differs",
    )
    _require(
        int(report["physicsTicksPerSecond"])
        == int(configuration.document["simulation"]["physicsTicksPerSecond"]),
        "XR physics rate differs",
    )
    _require(bool(report["worldSpaceHud"]), "XR world-space HUD is missing")
    _require(save["schema"] == SANDBOX_SAVE_SCHEMA, "XR save schema differs")
    _require(save["equippedWeaponFormId"] == loadout["weaponFormId"], "XR weapon identity differs")
    _require(save["weaponAmmoFormId"] == loadout["ammoFormId"], "XR ammunition identity differs")
    _require(int(save["weaponDamage"]) == int(loadout["damage"]), "XR weapon damage differs")
    _require(int(save["weaponClipSize"]) == int(loadout["clipSize"]), "XR clip size differs")
    _require(
        int(save["ammoInMagazine"]) == int(loadout["expectedAmmoInMagazineAfterReload"]),
        "XR magazine outcome differs",
    )
    _require(
        int(save["reserveAmmo"]) == int(loadout["expectedReserveRoundsAfterReload"]),
        "XR reserve outcome differs",
    )
    _require(int(save["shotsFired"]) == int(loadout["expectedShotsFired"]), "XR shot count differs")


def _owned_documents(
    install_manifest_path: Path,
) -> tuple[dict[str, object], list[dict[str, object]], dict[str, object]]:
    install = _read(install_manifest_path)
    primary = _read(Path(str(install["outputs"]["cellScene"])))
    linked = [_read(Path(str(row["scene"]))) for row in primary.get("linkedCells", [])]
    actors = _read(Path(str(install["outputs"]["actorScenes"])))
    return primary, linked, actors


def _active_references(scene: dict[str, object]) -> list[dict[str, object]]:
    return [row for row in scene["references"] if not bool(row["initiallyDisabled"])]


def _actor_count(actors: dict[str, object], accepted_cells: set[str]) -> int:
    return sum(1 for row in actors["actors"] if str(row["cellFormId"]) in accepted_cells)


def validate_cell_report(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
    *,
    require_traversal: bool,
) -> tuple[dict[str, object], dict[str, object]]:
    _verify_configuration(report, configuration)
    primary, linked, actors = _owned_documents(install_manifest_path)
    scenes = [primary, *linked]
    expected_assets = sum(len(scene["assets"]) for scene in scenes)
    expected_textures = sum(len(scene["textures"]) for scene in scenes)
    expected_materials = sum(int(scene["coverage"]["materialBindings"]) for scene in scenes)
    expected_references = sum(len(_active_references(scene)) for scene in scenes)
    expected_doors = sum(
        1
        for scene in scenes
        for row in _active_references(scene)
        if isinstance(row.get("interaction"), dict) and row["interaction"].get("type") == "door"
    )
    expected_lights = sum(int(scene["coverage"]["authoredLights"]) for scene in scenes)
    primary_cell = str(primary["cell"]["formId"])
    expected_primary_actors = _actor_count(actors, {primary_cell})
    _require(report.get("schema") == CELL_REPORT_SCHEMA, "Unexpected cell report schema")
    _require(report.get("status") == "pass", "Cell report did not pass")
    _require(report["cellFormId"] == primary_cell, "Cell identity differs from owned data")
    _require(int(report["assets"]) == expected_assets, "Loaded asset count differs from manifests")
    _require(int(report["textures"]) == expected_textures, "Loaded texture count differs from manifests")
    _require(int(report["materialBindings"]) == expected_materials, "Loaded material count differs from manifests")
    _require(int(report["references"]) == expected_references, "Loaded reference count differs from manifests")
    _require(int(report["doors"]) == expected_doors, "Loaded door count differs from manifests")
    _require(int(report["authoredLights"]) == expected_lights, "Loaded light count differs from manifests")
    _require(int(report["actors"]) == expected_primary_actors, "Primary-cell actor count differs")
    _require(len(report["linkedCells"]) == len(linked), "Linked-cell count differs")
    _require(len(report["portals"]) == len(primary.get("linkedCells", [])), "Portal count differs")
    _require(int(report["collisionMeshes"]) > 0, "No collision meshes were loaded")
    _require(int(report["surfaces"]) > 0, "No rendering surfaces were loaded")
    _require(int(report["vertices"]) > 0, "No rendering vertices were loaded")
    _require(bool(report["connectedAuthoredSpaces"]), "Authored spaces are not connected")
    linked_reports = {str(row["cellFormId"]): row for row in report["linkedCells"]}
    for scene in linked:
        cell_id = str(scene["cell"]["formId"])
        row = linked_reports[cell_id]
        accepted_cells = {str(value) for value in scene["cell"].get("sourceCellFormIds", [cell_id])}
        _require(int(row["assets"]) == len(scene["assets"]), f"Linked asset count differs: {cell_id}")
        _require(
            int(row["references"]) == len(_active_references(scene)),
            f"Linked reference count differs: {cell_id}",
        )
        _require(
            int(row["actors"]) == _actor_count(actors, accepted_cells),
            f"Linked actor count differs: {cell_id}",
        )
    proof = configuration.document["proof"]
    for portal in report["portals"]:
        _require(bool(portal["reciprocal"]), "Portal link is not reciprocal")
        _require(
            float(portal["alignmentErrorMeters"]) <= float(proof["portalAlignmentToleranceMeters"]),
            "Portal alignment exceeds policy",
        )
        _require(
            float(portal["normalAgreement"]) >= float(proof["portalNormalAgreementMinimum"]),
            "Portal normals disagree",
        )
    if require_traversal:
        traversal = report["doorTraversal"]
        _require(traversal is not None and traversal["status"] == "pass", "Traversal proof is missing")
        _require(bool(traversal["floorHit"]), "Floor proof did not hit authored collision")
        _require(
            abs(float(traversal["floorY"])) <= float(proof["spawnFloorToleranceMeters"]),
            "Floor proof elevation exceeds policy",
        )
        _require(bool(traversal["closedHitDoor"]), "Closed door did not block the proof ray")
        _require(not bool(traversal["openHit"]), "Open door blocked the proof ray")
        _require(bool(traversal["projectilePortalClear"]), "Projectile did not clear the portal")
        _require(bool(traversal["capsuleWalkThrough"]), "Capsule did not traverse the portal")
    return primary, actors


def validate_vr_layout(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
) -> None:
    validate_cell_report(report, install_manifest_path, configuration, require_traversal=False)
    presentation = report["xrPresentation"]
    loadout = configuration.document["xr"]["diagnosticRigProof"]
    _require(presentation is not None, "VR presentation report is missing")
    _require(bool(presentation["heldWeapon"]), "VR held weapon is missing")
    _require(bool(presentation["muzzleFeedback"]), "VR muzzle feedback is missing")
    _require(bool(presentation["wristHud"]), "VR wrist HUD is missing")
    _require(
        float(presentation["wristHudPixelSize"])
        <= float(configuration.document["hud"]["xrPixelSizeMeters"]),
        "VR wrist HUD pixel size exceeds policy",
    )
    state = presentation["startingLoadout"]
    _require(state["equippedWeaponFormId"] == loadout["weaponFormId"], "VR weapon identity differs")
    _require(int(state["weaponDamage"]) == int(loadout["damage"]), "VR weapon damage differs")
    _require(int(state["weaponClipSize"]) == int(loadout["clipSize"]), "VR clip size differs")
    _require(int(state["ammoInMagazine"]) == int(loadout["clipSize"]), "VR magazine start differs")
    _require(int(state["reserveAmmo"]) == int(loadout["reserveRounds"]), "VR reserve start differs")


def validate_gameplay_report(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
    expected_phase: str,
) -> None:
    _verify_configuration(report, configuration)
    primary, _linked, _actors = _owned_documents(install_manifest_path)
    route = configuration.document["proof"]["gameplayRoute"]
    weapon_rows = [
        row
        for row in primary["references"]
        if row["baseFormId"] == route["weaponPickupFormId"]
        and isinstance(row.get("interaction"), dict)
        and isinstance(row["interaction"].get("weapon"), dict)
    ]
    _require(len(weapon_rows) == 1, "Owned data did not resolve one gameplay proof weapon")
    weapon = weapon_rows[0]["interaction"]["weapon"]
    state = report["session"]
    _require(report.get("schema") == GAMEPLAY_REPORT_SCHEMA, "Unexpected gameplay report schema")
    _require(report.get("status") == "pass", "Gameplay report did not pass")
    _require(report.get("phase") == expected_phase, "Gameplay report phase differs")
    _require(bool(state["objectiveComplete"]), "Gameplay objective is incomplete")
    _require(state["equippedWeaponFormId"] == route["weaponPickupFormId"], "Gameplay weapon differs")
    _require(state["weaponAmmoFormId"] == weapon["ammoFormId"], "Gameplay ammunition differs")
    _require(int(state["weaponDamage"]) == int(weapon["damage"]), "Gameplay damage differs from WEAP")
    _require(int(state["weaponClipSize"]) == int(weapon["clipSize"]), "Gameplay clip differs from WEAP")
    _require(int(state["ammoInMagazine"]) == int(route["expectedAmmoInMagazine"]), "Gameplay magazine differs")
    _require(int(state["shotsFired"]) == int(route["expectedShotsFired"]), "Gameplay shot count differs")
    _require(
        int(state["emptiedContainers"]) == int(route["expectedEmptiedContainers"]),
        "Gameplay emptied-container count differs",
    )
    _require(int(state["openDoors"]) == int(route["expectedOpenDoors"]), "Gameplay open-door count differs")


def validate_pool_report(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
    expected_adapter: str,
) -> None:
    _verify_configuration(report, configuration)
    primary, _linked, _actors = _owned_documents(install_manifest_path)
    pool = primary.get("poolGameplay")
    _require(isinstance(pool, dict), "Owned cell has no pool gameplay manifest")
    table = pool["table"]
    balls = pool["balls"]
    assets = {str(asset["id"]): asset for asset in primary["assets"]}
    expected_masses = []
    for ball in balls:
        sidecar = _read(Path(str(assets[str(ball["authoredAssetId"])]["sidecar"])))
        bodies = sidecar["coverage"]["dynamicPhysicsBodies"]
        _require(len(bodies) == 1, "Owned pool ball has no unique dynamic body")
        expected_masses.append(float(bodies[0]["mass"]))

    _require(report.get("schema") == POOL_REPORT_SCHEMA, "Unexpected pool report schema")
    _require(report.get("status") == "pass", "Pool report did not pass")
    _require(report["cellFormId"] == primary["cell"]["formId"], "Pool CELL identity differs")
    _require(
        report["tableReferenceFormId"] == table["referenceFormId"],
        "Pool table reference differs",
    )
    _require(
        report["presentationModelPath"] == table["presentationModelPath"],
        "Pool table presentation model differs",
    )
    _require(
        report["gameplayCollisionSource"] == table["gameplayCollisionSource"],
        "Pool table collision source differs",
    )
    _require(int(report["authoredBalls"]) == len(balls), "Pool ball count differs")
    _require(int(report["dynamicConvexBodies"]) == len(balls), "Pool body count differs")
    actual_masses = [float(value) for value in report["massKilograms"]]
    _require(len(actual_masses) == len(expected_masses), "Pool mass count differs")
    _require(
        all(
            math.isclose(actual, expected, abs_tol=FLOAT_COMPARISON_TOLERANCE)
            for actual, expected in zip(sorted(actual_masses), sorted(expected_masses))
        ),
        "Pool masses differ from NIF bodies",
    )
    _require(report["inputAdapter"] == expected_adapter, "Pool input adapter differs")
    _require(bool(report["sharedSimulation"]), "Pool simulation is not shared")
    _require(bool(report["cueMounted"]), "Pool cue was not mounted")
    _require(bool(report["strikeAccepted"]), "Pool strike was not accepted")
    _require(int(report["cueBallBallCollisions"]) >= 1, "Pool ball contact is missing")
    _require(bool(report["authoredReset"]), "Pool authored reset failed")
    _require(not bool(report["hardwareValidated"]), "Software pool proof claimed hardware validation")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--mode",
        choices=(
            "xr",
            "cell",
            "vr-layout",
            "gameplay",
            "gameplay-reload",
            "pool-flat",
            "pool-xr-layout",
        ),
        required=True,
    )
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--install-manifest", type=Path)
    args = parser.parse_args()
    configuration = load_runtime_configuration()
    report = _read(args.report)
    if args.mode == "xr":
        validate_xr_report(report, configuration)
    else:
        if args.install_manifest is None:
            raise ValueError(f"{args.mode} validation requires --install-manifest")
        if args.mode == "cell":
            validate_cell_report(report, args.install_manifest, configuration, require_traversal=True)
        elif args.mode == "vr-layout":
            validate_vr_layout(report, args.install_manifest, configuration)
        elif args.mode == "gameplay":
            validate_gameplay_report(report, args.install_manifest, configuration, "first-run")
        elif args.mode == "gameplay-reload":
            validate_gameplay_report(report, args.install_manifest, configuration, "cold-reload")
        else:
            validate_pool_report(
                report,
                args.install_manifest,
                configuration,
                "desktop-look-and-power"
                if args.mode == "pool-flat"
                else "openxr-tracked-cue-layout",
            )
    print(f"OPENNV_RUNTIME_REPORT_PASS mode={args.mode} report={args.report.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
