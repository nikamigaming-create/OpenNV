extends SceneTree


const CONTRACT_SCHEMA := "opennv-ttw-fo3-source-authored-cg00-to-cg01-traversal/v1"
const CONTRACT_STATUS := "source-authored-root-traversal-player-collision-controller-unresolved"
const STATIC_PROOF_SCHEMA := "opennv-ttw-fo3-stage10-static-world-collision-readiness/v1"
const SAVE_SCHEMA := "opennv-ttw-fo3-source-traversal-save/v1"
const REPORT_SCHEMA := "opennv-ttw-fo3-source-traversal-headless-proof/v1"
const CAMPAIGN := "Fallout3"
const EDITION := "TTW"
const CG01_QUEST_EDITOR_ID := "CG01"
const CG01_MOVEMENT_STAGE := 10
const VECTOR_COMPONENTS := 3
const MODE_APPLY := "apply"
const MODE_RESTORE := "restore"


var _contract_path := ""
var _static_proof_path := ""
var _mode := ""
var _save_path := ""
var _report_path := ""


func _initialize() -> void:
	var arguments := OS.get_cmdline_user_args()
	var index := 0
	while index < arguments.size():
		var argument := arguments[index]
		if argument == "--contract" and index + 1 < arguments.size():
			_contract_path = arguments[index + 1]
			index += 2
		elif argument == "--static-collision-proof" and index + 1 < arguments.size():
			_static_proof_path = arguments[index + 1]
			index += 2
		elif argument == "--mode" and index + 1 < arguments.size():
			_mode = arguments[index + 1]
			index += 2
		elif argument == "--save" and index + 1 < arguments.size():
			_save_path = arguments[index + 1]
			index += 2
		elif argument == "--report" and index + 1 < arguments.size():
			_report_path = arguments[index + 1]
			index += 2
		else:
			_fail("unsupported argument: %s" % argument)
			return
	if _contract_path.is_empty() or _static_proof_path.is_empty() \
		or _save_path.is_empty() or _report_path.is_empty() \
		or _mode not in [MODE_APPLY, MODE_RESTORE]:
		_fail("--contract, --static-collision-proof, --mode, --save and --report are required")
		return
	_contract_path = ProjectSettings.globalize_path(_contract_path)
	_static_proof_path = ProjectSettings.globalize_path(_static_proof_path)
	_save_path = ProjectSettings.globalize_path(_save_path)
	_report_path = ProjectSettings.globalize_path(_report_path)
	call_deferred("_run")


func _run() -> void:
	var contract = _read_json(_contract_path)
	var static_proof = _read_json(_static_proof_path)
	if not contract is Dictionary or not static_proof is Dictionary:
		return
	if not _validate_contract(contract, static_proof):
		return
	if FileAccess.file_exists(_report_path):
		_fail("refusing to overwrite proof report: %s" % _report_path)
		return
	if _mode == MODE_APPLY:
		_apply(contract, static_proof)
	else:
		_restore(contract, static_proof)


func _validate_contract(contract: Dictionary, static_proof: Dictionary) -> bool:
	if contract.get("schema") != CONTRACT_SCHEMA \
		or contract.get("status") != CONTRACT_STATUS \
		or contract.get("campaign") != CAMPAIGN \
		or contract.get("edition") != EDITION \
		or contract.get("runtimeReady") \
		or contract.get("ownedPayloadsEmitted"):
		_fail("source traversal contract identity/readiness differs")
		return false
	var identity = contract.get("identity")
	var cg00 = contract.get("cg00Stage10")
	var cg01 = contract.get("cg01Stage10Traversal")
	var readiness = contract.get("readiness")
	var plan = contract.get("headlessProofPlan")
	if not identity is Dictionary or not cg00 is Dictionary \
		or not cg01 is Dictionary or not readiness is Dictionary \
		or not plan is Dictionary:
		_fail("source traversal contract shape differs")
		return false
	if identity.get("standaloneFallout3Accepted") \
		or identity.get("standaloneNewVegasAccepted") \
		or cg00.get("stage") != CG01_MOVEMENT_STAGE \
		or cg00["controls"].get("movementEnabled") \
		or cg00["camera1st"].get("camera3dProjectionEmitted") \
		or not cg00.get("rootPlacementReady") \
		or not cg00.get("controllerPhaseReadyAtStageEntry"):
		_fail("CG00 source-authored camera/package gate differs")
		return false
	var navigation = cg01.get("navigation")
	var route = navigation.get("route") if navigation is Dictionary else null
	var waypoints = route.get("waypoints") if route is Dictionary else null
	if not waypoints is Array or waypoints.is_empty() \
		or not cg01["controls"].get("movementEnabled") \
		or not cg01.get("rootTraversalReady") \
		or cg01.get("physicalPlayerCollisionReady") \
		or not readiness.get("staticCollisionShellReady") \
		or not readiness.get("sourceAuthoredCg01Stage10RootTraversalReady") \
		or readiness.get("physicalPlayerCollisionReady") \
		or readiness.get("runtimeReady"):
		_fail("CG01 source-authored route/readiness gate differs")
		return false
	var apply_index := int(plan.get("applyCheckpointWaypointIndex", -1))
	var final_index := int(plan.get("restoreFinalWaypointIndex", -1))
	if apply_index <= 0 or apply_index >= waypoints.size() \
		or final_index != waypoints.size() \
		or plan.get("movementAuthority") != "exact-navm-root-waypoints-no-player-body-proxy":
		_fail("source traversal proof plan differs")
		return false
	if static_proof.get("schema") != STATIC_PROOF_SCHEMA \
		or static_proof.get("campaign") != CAMPAIGN \
		or static_proof.get("edition") != EDITION \
		or not static_proof.get("headlessStaticWorldCollisionReadinessPassed") \
		or static_proof.get("playerTraversalExecuted") \
		or static_proof.get("playerTraversalReady"):
		_fail("static collision proof gate differs")
		return false
	var static_descriptor = identity.get("staticCollisionProof")
	var artifact_descriptor = identity.get("staticWorldArtifact")
	if not static_descriptor is Dictionary or not artifact_descriptor is Dictionary \
		or static_descriptor.get("sha256") != FileAccess.get_sha256(_static_proof_path) \
		or static_proof["artifact"].get("sha256") != artifact_descriptor.get("sha256"):
		_fail("static collision artifact/proof hash join differs")
		return false
	return true


func _apply(contract: Dictionary, static_proof: Dictionary) -> void:
	if FileAccess.file_exists(_save_path):
		_fail("refusing to overwrite traversal save: %s" % _save_path)
		return
	var route: Dictionary = contract["cg01Stage10Traversal"]["navigation"]["route"]
	var waypoints: Array = route["waypoints"]
	var waypoint_index: int = contract["headlessProofPlan"]["applyCheckpointWaypointIndex"]
	var position := _position(waypoints[waypoint_index - 1])
	var state := _new_state(contract, waypoint_index, position)
	if not _write_json(_save_path, state):
		return
	var report := _report(
		contract,
		static_proof,
		state,
		0,
		waypoint_index,
		false,
		false,
	)
	report["checkpointSaved"] = true
	report["coldRestorePassed"] = false
	_write_report(report)


func _restore(contract: Dictionary, static_proof: Dictionary) -> void:
	var state = _read_json(_save_path)
	if not state is Dictionary or not _validate_save(contract, state):
		return
	var route: Dictionary = contract["cg01Stage10Traversal"]["navigation"]["route"]
	var waypoints: Array = route["waypoints"]
	var start_index: int = state["routeWaypointIndex"]
	var final_index: int = contract["headlessProofPlan"]["restoreFinalWaypointIndex"]
	state["routeWaypointIndex"] = final_index
	state["playerRootPositionGodotGameUnits"] = _position(waypoints[final_index - 1])
	state["coldRestoreCount"] = int(state["coldRestoreCount"]) + 1
	if not _write_json(_save_path, state, true):
		return
	var no_replay := int(state["stage10ApplicationCount"]) == 1 \
		and int(state["autosaveCount"]) == 1
	var report := _report(
		contract,
		static_proof,
		state,
		start_index,
		final_index,
		true,
		no_replay,
	)
	report["checkpointSaved"] = true
	report["coldRestorePassed"] = no_replay
	report["rootReachedProjectedDadTrigger"] = true
	report["stage12TriggerExecuted"] = false
	_write_report(report)


func _new_state(contract: Dictionary, waypoint_index: int, position: Array) -> Dictionary:
	var identity: Dictionary = contract["identity"]
	return {
		"schema": SAVE_SCHEMA,
		"campaign": CAMPAIGN,
		"edition": EDITION,
		"pluginStackId": identity["pluginStackId"],
		"saveCompatibilityId": identity["saveCompatibilityId"],
		"sourceTraversalContractSha256": FileAccess.get_sha256(_contract_path),
		"staticWorldArtifactSha256": identity["staticWorldArtifact"]["sha256"],
		"staticCollisionProofSha256": identity["staticCollisionProof"]["sha256"],
		"questEditorId": CG01_QUEST_EDITOR_ID,
		"stage": CG01_MOVEMENT_STAGE,
		"routeWaypointIndex": waypoint_index,
		"playerRootPositionGodotGameUnits": position,
		"objective10Displayed": true,
		"stage10ApplicationCount": 1,
		"autosaveCount": 1,
		"coldRestoreCount": 0,
	}


func _validate_save(contract: Dictionary, state: Dictionary) -> bool:
	var identity: Dictionary = contract["identity"]
	var waypoint_count: int = contract["cg01Stage10Traversal"]["navigation"]["route"]["waypoints"].size()
	if state.get("schema") != SAVE_SCHEMA \
		or state.get("campaign") != CAMPAIGN \
		or state.get("edition") != EDITION \
		or state.get("pluginStackId") != identity["pluginStackId"] \
		or state.get("saveCompatibilityId") != identity["saveCompatibilityId"] \
		or state.get("sourceTraversalContractSha256") != FileAccess.get_sha256(_contract_path) \
		or state.get("staticWorldArtifactSha256") != identity["staticWorldArtifact"]["sha256"] \
		or state.get("staticCollisionProofSha256") != identity["staticCollisionProof"]["sha256"] \
		or state.get("questEditorId") != CG01_QUEST_EDITOR_ID \
		or int(state.get("stage", -1)) != CG01_MOVEMENT_STAGE \
		or int(state.get("routeWaypointIndex", -1)) <= 0 \
		or int(state.get("routeWaypointIndex", -1)) >= waypoint_count \
		or state.get("objective10Displayed") is not bool \
		or not state["objective10Displayed"] \
		or int(state.get("stage10ApplicationCount", -1)) != 1 \
		or int(state.get("autosaveCount", -1)) != 1 \
		or int(state.get("coldRestoreCount", -1)) != 0:
		_fail("source traversal save identity/state differs")
		return false
	var expected: Array = contract["cg01Stage10Traversal"]["navigation"]["route"]["waypoints"][int(state["routeWaypointIndex"]) - 1]["positionGodotGameUnits"]
	if not _same_position(state.get("playerRootPositionGodotGameUnits"), expected):
		_fail("source traversal saved root position differs")
		return false
	return true


func _report(
	contract: Dictionary,
	static_proof: Dictionary,
	state: Dictionary,
	start_index: int,
	end_index: int,
	restored: bool,
	no_replay: bool,
) -> Dictionary:
	return {
		"schema": REPORT_SCHEMA,
		"status": "passed-source-authored-navm-root-traversal-with-static-collision-shell-join",
		"mode": _mode,
		"campaign": CAMPAIGN,
		"edition": EDITION,
		"sourceTraversalContract": {
			"file": _contract_path,
			"bytes": FileAccess.get_file_as_bytes(_contract_path).size(),
			"sha256": FileAccess.get_sha256(_contract_path),
			"schema": CONTRACT_SCHEMA,
		},
		"staticCollisionProof": {
			"file": _static_proof_path,
			"bytes": FileAccess.get_file_as_bytes(_static_proof_path).size(),
			"sha256": FileAccess.get_sha256(_static_proof_path),
			"schema": STATIC_PROOF_SCHEMA,
			"physicsQueries": static_proof["coverage"]["physicsQueries"],
			"physicsQueryHits": static_proof["coverage"]["physicsQueryHits"],
		},
		"save": {
			"file": _save_path,
			"sha256": FileAccess.get_sha256(_save_path),
			"schema": SAVE_SCHEMA,
		},
		"pluginStackId": state["pluginStackId"],
		"saveCompatibilityId": state["saveCompatibilityId"],
		"questEditorId": state["questEditorId"],
		"stage": state["stage"],
		"routeWaypointStartIndex": start_index,
		"routeWaypointEndIndex": end_index,
		"routeWaypointMovesExecuted": end_index - start_index,
		"playerRootPositionGodotGameUnits": state["playerRootPositionGodotGameUnits"],
		"staticCollisionShellHashJoined": true,
		"staticCollisionQueriesPassed": true,
		"navmRootTraversalExecuted": end_index > start_index,
		"saveIdentityValidated": true,
		"restored": restored,
		"stage10ApplicationCount": state["stage10ApplicationCount"],
		"autosaveCount": state["autosaveCount"],
		"noStageOrAutosaveReplay": no_replay,
		"actorProxyCreated": false,
		"cameraCreated": false,
		"physicalPlayerCollisionExecuted": false,
		"physicalPlayerCollisionReady": false,
		"runtimeReady": false,
		"remainingBlockers": contract["remainingBlockers"],
	}


func _position(waypoint: Dictionary) -> Array:
	var value = waypoint.get("positionGodotGameUnits")
	if not value is Array or value.size() != VECTOR_COMPONENTS:
		_fail("source traversal waypoint position differs")
		return []
	return value.duplicate()


func _same_position(left, right) -> bool:
	if not left is Array or not right is Array \
		or left.size() != VECTOR_COMPONENTS or right.size() != VECTOR_COMPONENTS:
		return false
	for index in range(VECTOR_COMPONENTS):
		if not is_equal_approx(float(left[index]), float(right[index])):
			return false
	return true


func _write_report(report: Dictionary) -> void:
	if _write_json(_report_path, report):
		print(
			"TTW_FO3_SOURCE_TRAVERSAL_%s_PASS report=%s waypoint=%d" % [
				_mode.to_upper(),
				_report_path,
				report["routeWaypointEndIndex"],
			]
		)
		quit(0)


func _write_json(path: String, value: Dictionary, replace := false) -> bool:
	if FileAccess.file_exists(path) and not replace:
		_fail("refusing to overwrite JSON output: %s" % path)
		return false
	var output := FileAccess.open(path, FileAccess.WRITE)
	if output == null:
		_fail("could not open JSON output: %s" % path)
		return false
	output.store_string(JSON.stringify(value, "  ", true) + "\n")
	output.close()
	return true


func _read_json(path: String):
	if not FileAccess.file_exists(path):
		_fail("JSON input is absent: %s" % path)
		return null
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if parsed == null:
		_fail("JSON input is invalid: %s" % path)
	return parsed


func _fail(detail: String) -> void:
	printerr("TTW_FO3_SOURCE_TRAVERSAL_FAIL %s" % detail)
	quit(2)
