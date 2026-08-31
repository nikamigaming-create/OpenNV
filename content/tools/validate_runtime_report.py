#!/usr/bin/env python3
"""Validate runtime proof reports against owned-data manifests and shared policy."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from runtime_configuration import RuntimeConfiguration, load_runtime_configuration


CELL_REPORT_SCHEMA = "opennv-godot-cell/v1"
XR_REPORT_SCHEMA = "opennv-openxr-rig/v3"
XR_SIMULATOR_REPORT_SCHEMA = "opennv-openxr-simulator-acceptance/v1"
FLAT_CONTROLS_REPORT_SCHEMA = "opennv-flat-controls-acceptance/v2"
FLAT_ROUTE_TRAVEL_REPORT_SCHEMA = "opennv-flat-route-travel/v1"
GAMEPLAY_REPORT_SCHEMA = "opennv-godot-playable-route/v1"
CAMPAIGN_SAVE_SCHEMA = "opennv-campaign-save/v7"
POOL_REPORT_SCHEMA = "opennv-pool-practice/v1"
WORLD_PICKUP_REPORT_SCHEMA = "opennv-world-pickup-interaction/v1"
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
    _require(report.get("evidenceLevel") == "layout-only", "XR layout evidence level differs")
    _require(not bool(report["hardwareHeadsetValidated"]), "XR layout claimed headset validation")
    _require(not bool(report["windowsAppControlUsed"]), "XR layout used Windows app control")
    _require(not bool(report["foregroundInputInjected"]), "XR layout injected foreground input")
    _require(not bool(report["viewportXrEnabledDuringProof"]), "Headless XR proof enabled the viewport")
    _require(int(report["actionSets"]) == int(contract["expectedActionSetCount"]), "XR action-set count differs")
    _require(sorted(report["actionNames"]) == sorted(contract["actionNames"]), "XR action names differ")
    _require(
        sorted(report["testedInteractionProfiles"]) == sorted(contract["interactionProfilePaths"]),
        "XR interaction profiles differ",
    )
    _require(report["originType"] == "XROrigin3D", "XR origin type differs")
    _require(report["cameraType"] == "XRCamera3D", "XR camera type differs")
    _require(report["leftControllerType"] == "XRController3D", "XR left controller type differs")
    _require(report["rightControllerType"] == "XRController3D", "XR right controller type differs")
    _require(
        report["visibleProvider"] == "owned-data-required-at-cell-load",
        "XR layout visibility boundary differs",
    )
    _require(report["leftTracker"] == "left_hand", "XR left tracker differs")
    _require(report["rightTracker"] == "right_hand", "XR right tracker differs")
    _require(report["gripPose"] == "grip", "XR grip pose differs")
    _require(report["aimPose"] == "aim", "XR aim pose differs")
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
    _require(save["schema"] == CAMPAIGN_SAVE_SCHEMA, "XR save schema differs")
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


def _enabled_actor_rows(
    actors: dict[str, object], accepted_cells: set[str]
) -> list[dict[str, object]]:
    enabled = []
    for row in actors["actors"]:
        if str(row["cellFormId"]) not in accepted_cells:
            continue
        scene = _read(Path(str(row["scene"])))
        if not bool(scene["reference"]["initiallyDisabled"]):
            enabled.append(row)
    return enabled


def _actor_count(actors: dict[str, object], accepted_cells: set[str]) -> int:
    return len(_enabled_actor_rows(actors, accepted_cells))


def validate_cell_report(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
    *,
    require_traversal: bool,
    require_opening_menu: bool = False,
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
    accepted_actor_cells = {primary_cell}
    for scene in linked:
        accepted_actor_cells.update(
            str(value)
            for value in scene["cell"].get(
                "sourceCellFormIds", [scene["cell"]["formId"]]
            )
        )
    expected_actors = sorted(
        (
            str(row["referenceFormId"]),
            str(row["baseFormId"]),
            False,
            False,
        )
        for row in _enabled_actor_rows(actors, accepted_actor_cells)
    )
    actual_actors = sorted(
        (
            str(row["referenceFormId"]),
            str(row["baseFormId"]),
            bool(row["initiallyDisabled"]),
            bool(row["proofEnabled"]),
        )
        for row in report.get("actorPlacements", [])
    )
    _require(actual_actors == expected_actors, "Loaded actor identities or enable states differ")
    _require(
        [str(row["recipe"]) for row in actors["actors"]]
        == [
            str(recipe)
            for scene in scenes
            for recipe in scene.get("actorRecipes", [])
        ],
        "Actor manifest recipe closure differs from the ordered CELL route",
    )
    proof = configuration.document["proof"]
    _require(
        [
            (row["fromDoorReferenceFormId"], row["toDoorReferenceFormId"])
            for row in report["portals"]
        ]
        == [
            (row["fromDoorReferenceFormId"], row["toDoorReferenceFormId"])
            for row in primary.get("linkedCells", [])
        ],
        "Portal report order or identity differs from the owned route",
    )
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
        floor_y = float(traversal["floorY"])
        floor_normal = traversal["floorNormal"]
        _require(
            bool(traversal["floorOwnedCellCollision"]),
            "Floor proof did not hit collision owned by a loaded CELL",
        )
        _require(bool(traversal["floorWithinProbe"]), "Floor proof escaped its configured probe")
        _require(
            float(proof["spawnFloorRayEndMeters"]) - float(proof["spawnFloorToleranceMeters"])
            <= floor_y
            <= float(proof["spawnFloorRayStartMeters"]) + float(proof["spawnFloorToleranceMeters"]),
            "Floor proof elevation escaped its configured probe",
        )
        _require(
            bool(traversal["floorWalkable"])
            and len(floor_normal) == 3
            and float(floor_normal[1]) >= float(proof["walkableSurfaceNormalYMinimum"]),
            "Floor proof did not hit a walkable surface",
        )
        _require(bool(traversal["closedHitDoor"]), "Closed door did not block the proof ray")
        _require(not bool(traversal["openHit"]), "Open door blocked the proof ray")
        _require(bool(traversal["projectilePortalClear"]), "Projectile did not clear the portal")
        _require(bool(traversal["capsuleWalkThrough"]), "Capsule did not traverse the portal")
        traversed_portals = traversal.get("portals", [])
        expected_portals = primary.get("linkedCells", [])
        _require(
            len(traversed_portals) == len(expected_portals),
            "Per-hop traversal count differs from the owned route",
        )
        for actual, expected in zip(traversed_portals, expected_portals, strict=True):
            _require(
                actual["fromDoorReferenceFormId"] == expected["fromDoorReferenceFormId"]
                and actual["toDoorReferenceFormId"] == expected["toDoorReferenceFormId"],
                "Per-hop traversal order or identity differs",
            )
            _require(bool(actual["closedHitDoor"]), "Portal did not block while closed")
            _require(
                not bool(actual["openBlockedByPortalDoor"]),
                "Portal door still blocked while open",
            )
            _require(bool(actual["openRayPortalClear"]), "Open ray did not cross portal")
            _require(bool(actual["projectilePortalClear"]), "Projectile failed one portal hop")
            _require(bool(actual["floorHit"]), "Portal floor probe did not hit")
            _require(bool(actual["floorWalkable"]), "Portal floor is not walkable")
            _require(
                bool(actual["floorOwnedCellCollision"]),
                "Portal floor is not owned by the destination CELL",
            )
            _require(
                actual["traversalMode"] == "xtel-activation",
                "Linked portal did not use XTEL activation semantics",
            )
            _require(
                actual["capsuleWalkForward"] is None
                and actual["capsuleWalkBackward"] is None
                and actual["capsuleWalkThrough"] is None,
                "XTEL portal reported an inapplicable continuous capsule proof",
            )
    opening_menu = report.get("openingMenuProof")
    if require_opening_menu:
        _require(isinstance(opening_menu, dict), "Normal-menu Continue proof is missing")
    if opening_menu is not None:
        _require(opening_menu["action"] == "continue", "Normal-menu action differs")
        _require(
            opening_menu["inputTransport"] == "godot-owned-button-signal",
            "Normal-menu action bypassed the owned button signal",
        )
        _require(not bool(opening_menu["windowsAppControlUsed"]), "Windows app control was used")
        _require(
            not bool(opening_menu["foregroundInputInjected"]),
            "Foreground input was injected",
        )
        _require(bool(opening_menu["restoredCompleted"]), "Continue did not restore completion")
    return primary, actors


def validate_vr_layout(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
) -> None:
    validate_cell_report(report, install_manifest_path, configuration, require_traversal=False)
    presentation = report["xrPresentation"]
    primary, _linked, _actors = _owned_documents(install_manifest_path)
    first_person = primary["firstPerson"]
    loadout = first_person["startingLoadout"]
    rig = first_person["rig"]
    _require(presentation is not None, "VR presentation report is missing")
    _require(bool(presentation["heldWeapon"]), "VR held weapon is missing")
    _require(bool(presentation["muzzleFeedback"]), "VR muzzle feedback is missing")
    _require(bool(presentation["leftHandVisible"]), "VR left hand is missing")
    _require(bool(presentation["rightHandVisible"]), "VR right hand is missing")
    _require(
        presentation["visibleHandProvider"] == rig["provider"],
        "VR hand provider differs from owned data",
    )
    _require(presentation["leftGripPose"] == "grip", "VR left grip pose differs")
    _require(presentation["rightGripPose"] == "grip", "VR right grip pose differs")
    _require(presentation["leftAimPose"] == "aim", "VR left aim pose differs")
    _require(presentation["rightAimPose"] == "aim", "VR right aim pose differs")
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


def validate_xr_simulator_report(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
) -> None:
    _verify_configuration(report, configuration)
    primary, _linked, _actors = _owned_documents(install_manifest_path)
    xr = configuration.document["xr"]
    acceptance = xr["simulatorAcceptance"]
    loadout = primary["firstPerson"]["startingLoadout"]
    rig = primary["firstPerson"]["rig"]
    control = report["control"]
    gameplay = report["gameplay"]
    _require(report.get("schema") == XR_SIMULATOR_REPORT_SCHEMA, "Unexpected XR simulator schema")
    _require(report.get("status") == "pass", "XR simulator report did not pass")
    _require(report.get("evidenceLevel") == "simulator", "XR simulator evidence level differs")
    _require(not bool(report["hardwareHeadsetValidated"]), "XR simulator claimed headset validation")
    _require(not bool(report["windowsAppControlUsed"]), "XR simulator used Windows app control")
    _require(not bool(report["foregroundInputInjected"]), "XR simulator injected foreground input")
    _require(bool(report["leftActive"]) and bool(report["leftTracked"]), "XR left tracker is inactive")
    _require(bool(report["rightActive"]) and bool(report["rightTracked"]), "XR right tracker is inactive")
    _require(report["leftGripPose"] == "grip" and report["rightGripPose"] == "grip", "XR grip poses differ")
    _require(report["leftAimPose"] == "aim" and report["rightAimPose"] == "aim", "XR aim poses differ")
    _require(bool(report["leftHandVisible"]) and bool(report["rightHandVisible"]), "XR hands are missing")
    _require(report["visibleHandProvider"] == rig["provider"], "XR hand provider differs")
    _require(bool(report["heldWeapon"]), "XR 10mm presentation is missing")
    _require(bool(report["wristHud"]), "XR wrist HUD is missing")
    _require(int(report["openDoors"]) >= int(acceptance["minimumAcceptedActivations"]), "XR door did not open")
    _require(float(control["MaximumLocomotionMeters"]) >= float(acceptance["minimumLocomotionMeters"]), "XR locomotion is short")
    _require(float(control["MaximumLeftHandTravelMeters"]) >= float(acceptance["minimumHandTravelMeters"]), "XR left hand did not move")
    _require(float(control["MaximumRightHandTravelMeters"]) >= float(acceptance["minimumHandTravelMeters"]), "XR right hand did not move")
    _require(float(control["MaximumMoveStickMagnitude"]) >= float(xr["snapTurnActivationThreshold"]), "XR move stick is inactive")
    _require(float(control["MaximumTurnStickMagnitude"]) >= float(xr["snapTurnActivationThreshold"]), "XR turn stick is inactive")
    _require(bool(control["FloorObserved"]), "XR supported floor was not observed")
    _require(float(control["MaximumEyeHeightErrorMeters"]) <= float(acceptance["eyeHeightToleranceMeters"]), "XR eye height differs")
    _require(int(control["SnapTurns"]) >= int(acceptance["minimumSnapTurns"]), "XR snap turn count is short")
    _require(float(control["MaximumSnapPivotErrorMeters"]) <= float(acceptance["maximumSnapPivotErrorMeters"]), "XR snap pivot moved the HMD")
    _require(int(control["AcceptedActivations"]) >= int(acceptance["minimumAcceptedActivations"]), "XR activation was not accepted")
    _require(int(control["AcceptedFireActions"]) >= int(acceptance["minimumAcceptedFireActions"]), "XR fire was not accepted")
    _require(int(control["AcceptedReloadActions"]) >= int(acceptance["minimumAcceptedReloadActions"]), "XR reload was not accepted")
    _require(int(control["SaveEdges"]) >= int(acceptance["minimumSaveActions"]), "XR save was not accepted")
    _require(gameplay["schema"] == CAMPAIGN_SAVE_SCHEMA, "XR simulator save schema differs")
    _require(gameplay["equippedWeaponFormId"] == loadout["weaponFormId"], "XR simulator weapon differs")
    _require(int(gameplay["ammoInMagazine"]) == int(loadout["clipSize"]), "XR simulator reload outcome differs")
    _require(int(gameplay["reserveAmmo"]) == int(loadout["reserveRounds"]) - 1, "XR simulator reserve differs")
    _require(int(gameplay["shotsFired"]) == 1, "XR simulator shot count differs")


def validate_flat_controls_report(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
) -> None:
    _verify_configuration(report, configuration)
    primary, _linked, _actors = _owned_documents(install_manifest_path)
    desktop = configuration.document["player"]["desktopInput"]
    acceptance = desktop["acceptance"]
    first_person = primary["firstPerson"]
    loadout = first_person["startingLoadout"]
    gameplay = report["gameplay"]
    _require(report.get("schema") == FLAT_CONTROLS_REPORT_SCHEMA, "Unexpected flat controls schema")
    _require(report.get("status") == "pass", "Flat controls report did not pass")
    _require(report.get("inputTransport") == "godot-input-map-plus-parse-input-event", "Flat input transport differs")
    _require(not bool(report["windowsAppControlUsed"]), "Flat proof used Windows app control")
    _require(not bool(report["foregroundInputInjected"]), "Flat proof injected foreground input")
    _require(bool(report["mouseCaptured"]), "Flat mouse capture failed")
    _require(float(report["lookRadians"]) >= float(acceptance["minimumLookRadians"]), "Flat mouse look is short")
    _require(float(report["locomotionMeters"]) >= float(acceptance["minimumLocomotionMeters"]), "Flat locomotion is short")
    _require(bool(report["leftHandVisible"]) and bool(report["rightHandVisible"]), "Flat first-person hands are missing")
    _require(report["visibleHandProvider"] == first_person["rig"]["provider"], "Flat hand provider differs")
    _require(bool(report["heldWeapon"]), "Flat held weapon is missing")
    _require(bool(report["desktopHud"]), "Flat HUD is missing")
    pip_boy = report.get("pipBoy")
    _require(isinstance(pip_boy, dict), "Flat Pip-Boy report is missing")
    _require(
        bool(pip_boy["available"])
        and bool(pip_boy["opened"])
        and bool(pip_boy["closed"]),
        "Flat Pip-Boy did not open and close",
    )
    _require(int(report["activationEdges"]) == 1, "Flat activation edge count differs")
    expected_keys = {
        str(desktop[name]["action"]): str(desktop[name]["physicalKey"])
        for name in (
            "moveLeft",
            "moveRight",
            "moveForward",
            "moveBackward",
            "activate",
            "grab",
            "reload",
            "save",
            "cancel",
            "pipBoy",
        )
    }
    actual_keys = {str(row["Action"]): str(row["PhysicalKey"]) for row in report["keyBindings"]}
    _require(actual_keys == expected_keys, "Flat key bindings differ from configuration")
    expected_mouse = {
        str(desktop[name]["action"]): str(desktop[name]["button"])
        for name in ("fire", "captureMouse", "poolPowerUp", "poolPowerDown")
    }
    actual_mouse = {str(row["Action"]): str(row["Button"]) for row in report["mouseBindings"]}
    _require(actual_mouse == expected_mouse, "Flat mouse bindings differ from configuration")
    _require(gameplay["schema"] == CAMPAIGN_SAVE_SCHEMA, "Flat save schema differs")
    _require(gameplay["equippedWeaponFormId"] == loadout["weaponFormId"], "Flat weapon differs")
    _require(int(gameplay["ammoInMagazine"]) == int(loadout["clipSize"]), "Flat reload outcome differs")
    _require(int(gameplay["reserveAmmo"]) == int(loadout["reserveRounds"]) - 1, "Flat reserve differs")
    _require(int(gameplay["shotsFired"]) == 1, "Flat shot count differs")


def validate_flat_route_travel_report(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
    expected_phase: str,
    prior_report: dict[str, object] | None,
) -> None:
    _verify_configuration(report, configuration)
    primary, linked, _actors = _owned_documents(install_manifest_path)
    expected_portals = [
        {
            "fromCellFormId": str(row["fromCellFormId"]),
            "toCellFormId": str(row["cellFormId"]),
            "fromDoorReferenceFormId": str(row["fromDoorReferenceFormId"]),
            "toDoorReferenceFormId": str(row["toDoorReferenceFormId"]),
        }
        for row in primary.get("linkedCells", [])
    ]
    _require(expected_portals, "Owned route contains no portal links")
    expected_active = expected_portals[-1]["toCellFormId"]
    expected_editor = str(linked[-1]["cell"]["editorId"])
    gameplay = report["gameplay"]
    save = _read(Path(str(report["save"]["path"])))

    _require(
        report.get("schema") == FLAT_ROUTE_TRAVEL_REPORT_SCHEMA,
        "Unexpected flat route travel schema",
    )
    _require(report.get("status") == "pass", "Flat route travel did not pass")
    _require(report.get("phase") == expected_phase, "Flat route travel phase differs")
    _require(
        report.get("inputTransport")
        == "owned-menu-button-signal-plus-godot-input-map",
        "Flat route travel input transport differs",
    )
    _require(not bool(report["windowsAppControlUsed"]), "Route proof used Windows app control")
    _require(not bool(report["foregroundInputInjected"]), "Route proof injected foreground input")
    _require(report["orderedPortals"] == expected_portals, "Ordered portal route differs")
    _require(report["routeCellFormId"] == primary["cell"]["formId"], "Route root CELL differs")
    _require(report["activeCellFormId"] == expected_active, "Final active CELL differs")
    _require(report["activeCellEditorId"] == expected_editor, "Final active CELL editor ID differs")
    _require(gameplay["schema"] == CAMPAIGN_SAVE_SCHEMA, "Route save schema differs")
    _require(gameplay["activeCellFormId"] == expected_active, "Gameplay active CELL differs")
    _require(bool(gameplay["playerTransformRestored"]), "Menu Continue did not restore a transform")
    opening = gameplay["opening"]
    _require(
        isinstance(opening, dict)
        and int(opening["stage"]) == 200
        and bool(opening["completed"]),
        "Route travel did not retain the completed opening state",
    )
    _require(save["schema"] == CAMPAIGN_SAVE_SCHEMA, "Persisted route save schema differs")
    _require(save["cellFormId"] == primary["cell"]["formId"], "Persisted route root differs")
    _require(save["activeCellFormId"] == expected_active, "Persisted active CELL differs")
    _require(
        int(save["opening"]["Stage"]) == 200 and bool(save["opening"]["Completed"]),
        "Persisted opening completion differs",
    )
    expected_open_doors = {
        door
        for portal in expected_portals
        for door in (
            portal["fromDoorReferenceFormId"],
            portal["toDoorReferenceFormId"],
        )
    }
    actual_open_doors = {
        str(form_id)
        for form_id, opened in save["doorStates"].items()
        if bool(opened)
    }
    _require(
        expected_open_doors.issubset(actual_open_doors),
        "Persisted route door state is incomplete",
    )
    sunny = report.get("sunny")
    _require(
        isinstance(sunny, dict)
        and sunny["referenceFormId"] == "00104e85"
        and not bool(sunny["InitiallyDisabled"])
        and not bool(sunny["ProofEnabled"]),
        "Enabled owned Sunny state differs",
    )

    scenes = [primary, *linked]
    environment_set = report.get("environmentSet")
    _require(isinstance(environment_set, dict), "Route world-environment evidence is missing")
    _require(
        environment_set.get("policy")
        == "current-cell-world-environment-plus-owned-exterior-sky",
        "Route world-environment policy differs",
    )
    _require(
        environment_set.get("surfaceLightingPolicy")
        == "existing-compiled-cell-lighting-not-switched",
        "Route surface-lighting boundary differs",
    )
    _require(
        environment_set.get("activeCellFormId") == expected_active,
        "Route world-environment active CELL differs",
    )
    actual_spaces = {
        str(row["cellFormId"]): row for row in environment_set.get("spaces", [])
    }
    _require(
        set(actual_spaces) == {str(scene["cell"]["formId"]) for scene in scenes},
        "Route world-environment CELL set differs",
    )
    exterior_scenes = [scene for scene in scenes if not bool(scene["cell"]["interior"])]
    _require(len(exterior_scenes) == 1, "Route requires exactly one exterior environment")
    exterior = exterior_scenes[0]
    catalog = exterior["environmentCatalog"]
    default_weather_entries = [
        row
        for row in catalog["climate"]["weatherEntries"]
        if row["globalFormId"] is None and int(row["chance"]) == 100
    ]
    _require(
        len(default_weather_entries) == 1,
        "Owned route climate has no unique unconditional weather",
    )
    weather_form = str(default_weather_entries[0]["weatherFormId"])[2:].lower()
    weather_rows = [
        row for row in catalog["weather"]
        if str(row["formId"])[2:].lower() == weather_form
    ]
    _require(len(weather_rows) == 1, "Owned route default weather is missing")
    weather = weather_rows[0]
    atmosphere_sidecar = _read(Path(str(catalog["skyModels"]["atmosphere"]["sidecar"])))
    clouds_sidecar = _read(Path(str(catalog["skyModels"]["clouds"]["sidecar"])))
    exterior_space = actual_spaces[str(exterior["cell"]["formId"])]
    _require(
        exterior_space.get("mode") == configuration.document["exteriorEnvironment"]["mode"],
        "Route exterior mode differs",
    )
    _require(
        math.isclose(
            float(exterior_space["gameHour"]),
            float(catalog["climate"]["timing"]["sunriseEndHour"]),
            abs_tol=FLOAT_COMPARISON_TOLERANCE,
        ),
        "Route exterior configured day sample differs",
    )
    _require(exterior_space.get("weatherFormId") == weather_form, "Route WTHR differs")
    _require(
        exterior_space.get("weatherEditorId") == weather["editorId"],
        "Route WTHR editor ID differs",
    )
    _require(
        str(exterior_space.get("atmosphereSourceSha256", "")).lower()
        == str(atmosphere_sidecar["source"]["sha256"]).lower(),
        "Route atmosphere source differs",
    )
    _require(
        str(exterior_space.get("cloudsSourceSha256", "")).lower()
        == str(clouds_sidecar["source"]["sha256"]).lower(),
        "Route clouds source differs",
    )
    _require(
        int(exterior_space.get("boundCloudTextureLayers", -1))
        == sum(bool(path) for path in weather["cloudTextures"]),
        "Route bound cloud texture count differs",
    )
    for scene in scenes:
        if not bool(scene["cell"]["interior"]):
            continue
        space = actual_spaces[str(scene["cell"]["formId"])]
        _require(space.get("mode") == "interior-xcll", "Route interior XCLL mode differs")
        _require(
            space.get("weatherFormId") is None
            and space.get("atmosphereSourceSha256") is None
            and space.get("cloudsSourceSha256") is None
            and int(space.get("boundCloudTextureLayers", -1)) == 0,
            "Route interior unexpectedly reports exterior sky state",
        )
    expected_environment_updates = [str(primary["cell"]["formId"])]
    if expected_phase == "first-run":
        expected_environment_updates.extend(portal["toCellFormId"] for portal in expected_portals)
    else:
        expected_environment_updates = [expected_active]
    _require(
        [str(row["cellFormId"]) for row in environment_set.get("updates", [])]
        == expected_environment_updates,
        "Route world-environment update order differs",
    )

    transitions = report["transitions"]
    if expected_phase == "first-run":
        _require(
            [
                {
                    key: row[key]
                    for key in (
                        "fromCellFormId",
                        "toCellFormId",
                        "fromDoorReferenceFormId",
                        "toDoorReferenceFormId",
                    )
                }
                for row in transitions
            ]
            == expected_portals,
            "Player portal transition order or identity differs",
        )
        _require(prior_report is None, "First route phase cannot use a prior report")
    else:
        _require(bool(report["activeCellRestored"]), "Cold Continue did not restore active CELL")
        _require(not transitions, "Cold reload unexpectedly replayed portal transitions")
        _require(prior_report is not None, "Cold route phase requires the first-run report")
        _require(prior_report.get("phase") == "first-run", "Prior route phase differs")
        _require(
            prior_report["save"]["sha256"] == report["save"]["sha256"],
            "Cold Continue changed the persisted save",
        )
        first_transform = prior_report["playerTransform"]
        cold_transform = report["playerTransform"]
        _require(
            all(
                math.isclose(float(first), float(cold), abs_tol=FLOAT_COMPARISON_TOLERANCE)
                for first, cold in zip(
                    first_transform["position"], cold_transform["position"], strict=True
                )
            ),
            "Cold Continue player position differs",
        )
        first_rotation = [float(value) for value in first_transform["rotation"]]
        cold_rotation = [float(value) for value in cold_transform["rotation"]]
        quaternion_dot = abs(sum(
            first * cold for first, cold in zip(first_rotation, cold_rotation, strict=True)
        ))
        _require(
            math.isclose(quaternion_dot, 1.0, abs_tol=FLOAT_COMPARISON_TOLERANCE),
            "Cold Continue player rotation differs",
        )


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
    primary, linked, _actors = _owned_documents(install_manifest_path)
    owned_scenes = [primary, *linked]
    matches = [
        scene
        for scene in owned_scenes
        if scene["cell"]["formId"] == report.get("cellFormId")
    ]
    _require(len(matches) == 1, "Pool report CELL is outside the owned route")
    primary = matches[0]
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
    _require(bool(report["pocketDetected"]), "Pool pocket detection is missing")
    _require(bool(report["pocketSaveRestored"]), "Pool pocket save restore failed")
    _require(
        bool(report["liveStateRestoredFromColdSave"]),
        "Live pool state was not restored from the cold-loaded save",
    )
    _require(bool(report["authoredReset"]), "Pool authored reset failed")
    _require(not bool(report["hardwareValidated"]), "Software pool proof claimed hardware validation")


def validate_world_pickup_report(
    report: dict[str, object],
    install_manifest_path: Path,
    configuration: RuntimeConfiguration,
) -> None:
    _verify_configuration(report, configuration)
    primary, _linked, _actors = _owned_documents(install_manifest_path)
    pickup_references = {
        str(row["formId"]): row
        for row in primary["references"]
        if not bool(row["initiallyDisabled"])
        and isinstance(row.get("interaction"), dict)
        and row["interaction"].get("type") == "pickup"
    }
    selected = str(report["pickupReferenceFormId"])
    _require(report.get("schema") == WORLD_PICKUP_REPORT_SCHEMA, "Unexpected pickup report schema")
    _require(report.get("status") == "pass", "Pickup interaction report did not pass")
    _require(report["cellFormId"] == primary["cell"]["formId"], "Pickup CELL identity differs")
    _require(selected in pickup_references, "Pickup proof used a non-owned reference")
    _require(str(report["physicsSource"]).startswith("owned-nif-"), "Pickup physics is not owned NIF data")
    _require(int(report["exactOwnedDynamicPickups"]) > 0, "No movable owned pickup was proved")
    _require(
        int(report["activePickups"])
        == int(report["exactOwnedDynamicPickups"]) + int(report["unsupportedPickupPhysics"]),
        "Pickup physics coverage counts disagree",
    )
    desktop = configuration.document["player"]["desktopInput"]
    _require(report["desktopControl"] == desktop["grab"]["physicalKey"], "Pickup grab key differs")
    _require(report["collectControl"] == desktop["activate"]["physicalKey"], "Pickup collect key differs")
    _require(bool(report["heldCollisionSuppressed"]), "Held pickup collision policy failed")
    _require(bool(report["droppedCollisionRestored"]), "Dropped pickup collision was not restored")
    _require(bool(report["coldSaveRestored"]), "Moved pickup did not survive a cold save load")
    _require(not bool(report["hardwareValidated"]), "Software pickup proof claimed hardware validation")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--mode",
        choices=(
            "xr",
            "cell",
            "cell-menu-continue",
            "vr-layout",
            "gameplay",
            "gameplay-reload",
            "pool-flat",
            "pool-xr-layout",
            "world-pickup",
            "xr-simulator",
            "flat-controls",
            "flat-route-travel",
            "flat-route-reload",
        ),
        required=True,
    )
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--install-manifest", type=Path)
    parser.add_argument("--prior-report", type=Path)
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
        elif args.mode == "cell-menu-continue":
            validate_cell_report(
                report,
                args.install_manifest,
                configuration,
                require_traversal=True,
                require_opening_menu=True,
            )
        elif args.mode == "vr-layout":
            validate_vr_layout(report, args.install_manifest, configuration)
        elif args.mode == "gameplay":
            validate_gameplay_report(report, args.install_manifest, configuration, "first-run")
        elif args.mode == "gameplay-reload":
            validate_gameplay_report(report, args.install_manifest, configuration, "cold-reload")
        elif args.mode == "xr-simulator":
            validate_xr_simulator_report(report, args.install_manifest, configuration)
        elif args.mode == "flat-controls":
            validate_flat_controls_report(report, args.install_manifest, configuration)
        elif args.mode == "world-pickup":
            validate_world_pickup_report(report, args.install_manifest, configuration)
        elif args.mode in ("flat-route-travel", "flat-route-reload"):
            validate_flat_route_travel_report(
                report,
                args.install_manifest,
                configuration,
                "first-run" if args.mode == "flat-route-travel" else "cold-reload",
                _read(args.prior_report) if args.prior_report is not None else None,
            )
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
