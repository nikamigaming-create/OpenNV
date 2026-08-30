extends SceneTree


const ARTIFACT_SCHEMA := "opennv-ttw-fo3-cg00-stage10-godot-world-artifact/v1"
const COLLISION_SCHEMA := "opennv-ttw-fo3-stage10-collision/v1"
const REPORT_SCHEMA := "opennv-ttw-fo3-stage10-static-world-collision-readiness/v1"
const CAMPAIGN := "Fallout3"
const EDITION := "TTW"
const QUEST_EDITOR_ID := "CG00"
const TARGET_STAGE := 10
const PROOF_COLLISION_LAYER := 1
const RAY_MARGIN_GAME_UNITS := 10.0
const MINIMUM_TRIANGLE_AREA_SQUARED := 0.000001
const VECTOR_COMPONENTS := 3
const MAXIMUM_QUERY_CONTACTS := 8
const CONCAVE_PROBE_FRONT_FRACTION := 0.75


var _artifact_path := ""
var _report_path := ""
var _artifact_root := ""
var _world: Node3D
var _shape_counts := {}
var _body_counts := {}
var _queries := 0
var _query_hits := 0
var _probe_attempts := 0
var _tested_assets := 0
var _constraint_types := {}


func _initialize() -> void:
	var arguments := OS.get_cmdline_user_args()
	var index := 0
	while index < arguments.size():
		var argument := arguments[index]
		if argument == "--artifact" and index + 1 < arguments.size():
			_artifact_path = arguments[index + 1]
			index += 2
		elif argument == "--report" and index + 1 < arguments.size():
			_report_path = arguments[index + 1]
			index += 2
		else:
			_fail("unsupported argument: %s" % argument)
			return
	if _artifact_path.is_empty() or _report_path.is_empty():
		_fail("--artifact and --report are required")
		return
	_artifact_path = ProjectSettings.globalize_path(_artifact_path)
	_report_path = ProjectSettings.globalize_path(_report_path)
	_artifact_root = _artifact_path.get_base_dir()
	call_deferred("_run")


func _run() -> void:
	var artifact = _read_json(_artifact_path)
	if not artifact is Dictionary:
		_fail("artifact root is not an object")
		return
	if not _validate_artifact(artifact):
		return
	_world = Node3D.new()
	_world.name = "TTW_FO3_STAGE10_COLLISION_READINESS"
	root.add_child(_world)
	var models: Dictionary = artifact["assets"]["models"]
	var first_placement_by_asset := {}
	var collision_input_placements := 0
	var materialized_placements := 0
	var active_placements := 0
	for placement_value in artifact["godotWorld"]["cellShell"]:
		var placement: Dictionary = placement_value
		if not placement["authoredCollisionInput"]:
			continue
		collision_input_placements += 1
		if placement["collisionArtifactMaterialized"]:
			materialized_placements += 1
		else:
			_fail("collision input placement is not materialized")
			return
		if placement["collisionActive"]:
			active_placements += 1
		var asset_id: String = placement["artifactResourceId"]
		if not first_placement_by_asset.has(asset_id):
			first_placement_by_asset[asset_id] = placement

	for asset_id_value in first_placement_by_asset:
		var asset_id: String = asset_id_value
		var asset: Dictionary = models[asset_id]
		var publication: Dictionary = asset["collisionPublication"]
		var descriptor = publication.get("exactShapeContract")
		if not descriptor is Dictionary:
			_fail("collision asset has no exact shape contract: %s" % asset_id)
			return
		var contract_path := _artifact_root.path_join(descriptor["file"])
		if FileAccess.get_sha256(contract_path) != descriptor["sha256"]:
			_fail("collision contract hash differs: %s" % asset_id)
			return
		var contract = _read_json(contract_path)
		if not contract is Dictionary or not _validate_collision_contract(contract, asset):
			return
		var placement: Dictionary = first_placement_by_asset[asset_id]
		if not await _query_asset(asset_id, contract, placement, artifact["coordinates"]):
			return
		_tested_assets += 1

	var exact_assets: int = artifact["coverage"]["collisionPublicationAssets"]
	if _tested_assets != exact_assets:
		_fail("tested collision asset count differs: %d != %d" % [_tested_assets, exact_assets])
		return
	var report := {
		"schema": REPORT_SCHEMA,
		"status": "passed-exact-owned-collision-shape-publication-and-godot-query",
		"campaign": CAMPAIGN,
		"edition": EDITION,
		"stage": {"questEditorId": QUEST_EDITOR_ID, "stage": TARGET_STAGE},
		"artifact": {
			"file": _artifact_path,
			"bytes": FileAccess.get_file_as_bytes(_artifact_path).size(),
			"sha256": FileAccess.get_sha256(_artifact_path),
			"schema": ARTIFACT_SCHEMA,
		},
		"coverage": {
			"collisionInputPlacements": collision_input_placements,
			"collisionMaterializedPlacements": materialized_placements,
			"collisionActivePlacements": active_placements,
			"uniqueCollisionAssetsTested": _tested_assets,
			"bodyTypes": _body_counts,
			"shapeTypes": _shape_counts,
			"sourceConstraintTypesObserved": _constraint_types.keys(),
			"physicsQueries": _queries,
			"physicsQueryHits": _query_hits,
			"physicsProbeAttempts": _probe_attempts,
		},
		"sourceFiltersValidated": true,
		"renderMeshSubstitutionUsed": false,
		"dynamicBodiesFrozenForStaticReadinessProof": true,
		"engineDynamicsParityReady": false,
		"headlessStaticWorldCollisionReadinessPassed": _queries > 0 and _queries == _query_hits,
		"playerTraversalExecuted": false,
		"playerTraversalReady": false,
		"actorsPlacedOrVisible": false,
		"cameraEmitted": false,
		"runtimeReady": false,
		"remainingBlockers": [
			"live TTW stage-10 player/camera/participant transform and controller-phase contract absent",
			"player start and NAVM traversal join not materialized",
			"Havok dynamics/constraints and trigger filter mapping are outside this static collision proof",
			"source controller publication remains unexecuted",
		],
	}
	var output := FileAccess.open(_report_path, FileAccess.WRITE)
	if output == null:
		_fail("could not open report output")
		return
	output.store_string(JSON.stringify(report, "  ", true) + "\n")
	output.close()
	print("TTW_FO3_STATIC_COLLISION_READINESS_PASS report=%s queries=%d" % [_report_path, _queries])
	quit(0)


func _validate_artifact(artifact: Dictionary) -> bool:
	var stage = artifact.get("stage")
	if artifact.get("schema") != ARTIFACT_SCHEMA \
		or artifact.get("campaign") != CAMPAIGN \
		or artifact.get("edition") != EDITION \
		or not stage is Dictionary \
		or stage.get("questEditorId") != QUEST_EDITOR_ID \
		or int(stage.get("stage", -1)) != TARGET_STAGE:
		_fail("artifact identity differs: schema=%s campaign=%s edition=%s stage=%s" % [artifact.get("schema"), artifact.get("campaign"), artifact.get("edition"), stage])
		return false
	if artifact.get("runtimeArtifactsMaterialized") is not bool \
		or not artifact["runtimeArtifactsMaterialized"] \
		or not artifact["staticWorldTransportReady"] \
		or artifact["actorsPlacedOrVisible"] \
		or artifact["cameraEmitted"] \
		or artifact["runtimeReady"]:
		_fail("artifact readiness/isolation gate differs")
		return false
	return true


func _validate_collision_contract(contract: Dictionary, asset: Dictionary) -> bool:
	if contract.get("schema") != COLLISION_SCHEMA \
		or contract.get("sourceSha256") != asset["sourceSha256"] \
		or not contract.get("collisionReady", false) \
		or contract.get("renderMeshSubstitutionUsed", true) \
		or not contract.get("sourceFiltersPreserved", false):
		_fail("collision contract identity/readiness differs: %s" % asset["logicalPath"])
		return false
	for constraint_type in contract.get("sourceConstraintTypes", []):
		_constraint_types[constraint_type] = true
	for body_value in contract["bodies"]:
		var body: Dictionary = body_value
		var source_filter = body.get("filter")
		if not source_filter is Dictionary \
			or source_filter.get("layer", -1) < 0 \
			or source_filter.get("flagsAndPartNumber", -1) < 0 \
			or source_filter.get("unknownShort", -1) < 0:
			_fail("collision body filter provenance differs")
			return false
	return true


func _query_asset(
	asset_id: String,
	contract: Dictionary,
	placement: Dictionary,
	coordinates: Dictionary,
) -> bool:
	var body_nodes: Array[CollisionObject3D] = []
	var query_rows: Array[Dictionary] = []
	var shape_scale: float = placement["uniformScale"] * coordinates["worldUnitsToMeters"]
	for body_value in contract["bodies"]:
		var body_contract: Dictionary = body_value
		var body_node: CollisionObject3D
		if body_contract["dynamic"]:
			var dynamic_body := RigidBody3D.new()
			dynamic_body.freeze = true
			dynamic_body.freeze_mode = RigidBody3D.FREEZE_MODE_STATIC
			dynamic_body.mass = body_contract["mass"]
			body_node = dynamic_body
		else:
			body_node = StaticBody3D.new()
		body_node.name = "COLLISION_%s_%s" % [asset_id, body_contract["bodyBlock"]]
		body_node.collision_layer = PROOF_COLLISION_LAYER
		body_node.collision_mask = PROOF_COLLISION_LAYER
		body_node.transform = _placement_transform(placement, coordinates)
		_world.add_child(body_node)
		body_nodes.append(body_node)
		var body_type: String = body_contract["godotBodyType"]
		_body_counts[body_type] = _body_counts.get(body_type, 0) + 1
		for shape_value in body_contract["shapes"]:
			var shape_contract: Dictionary = shape_value
			var publication := _shape(shape_contract, shape_scale)
			if publication.is_empty():
				return false
			var collision_shape := CollisionShape3D.new()
			collision_shape.shape = publication["shape"]
			collision_shape.transform = publication["localTransform"]
			body_node.add_child(collision_shape)
			query_rows.append({
				"body": body_node,
				"probes": publication["probes"],
				"probeRadius": RAY_MARGIN_GAME_UNITS * shape_scale,
			})
			var shape_type: String = shape_contract["sourceShapeType"]
			_shape_counts[shape_type] = _shape_counts.get(shape_type, 0) + 1
	await physics_frame
	await physics_frame
	var direct_state := _world.get_world_3d().direct_space_state
	for query_row in query_rows:
		var body: CollisionObject3D = query_row["body"]
		var shape_hit := false
		for probe_position in query_row["probes"]:
			var probe := SphereShape3D.new()
			probe.radius = query_row["probeRadius"]
			var probe_transform := Transform3D(
				Basis.IDENTITY,
				body.global_transform * probe_position,
			)
			var query := PhysicsShapeQueryParameters3D.new()
			query.shape = probe
			query.transform = probe_transform
			query.collision_mask = PROOF_COLLISION_LAYER
			query.collide_with_bodies = true
			query.collide_with_areas = false
			var hits := direct_state.intersect_shape(query, MAXIMUM_QUERY_CONTACTS)
			_probe_attempts += 1
			if hits.any(func(hit: Dictionary) -> bool: return hit.get("collider") == body):
				shape_hit = true
				break
		_queries += 1
		if not shape_hit:
			_fail("Godot collision-shape query missed exact asset/body: %s/%s" % [asset_id, body.name])
			return false
		_query_hits += 1
	for body in body_nodes:
		body.queue_free()
	await process_frame
	return true


func _shape(contract: Dictionary, shape_scale: float) -> Dictionary:
	var shape_type: String = contract["godotShapeType"]
	var identity := Transform3D.IDENTITY
	if shape_type == "ConvexPolygonShape3D":
		var points := _vectors(contract["pointsGodotGameUnits"], shape_scale)
		if points.size() < 4:
			_fail("convex collision has too few points")
			return {}
		var shape := ConvexPolygonShape3D.new()
		shape.points = points
		var query := _inside_query(points, shape_scale)
		return {"shape": shape, "localTransform": identity, "query": query, "probes": PackedVector3Array([query[0]])}
	if shape_type == "ConcavePolygonShape3D":
		var points := _vectors(contract["pointsGodotGameUnits"], shape_scale)
		var faces := PackedVector3Array()
		var query := PackedVector3Array()
		var probes := PackedVector3Array()
		for triangle_value in contract["triangles"]:
			var triangle: Array = triangle_value
			if triangle.size() != VECTOR_COMPONENTS:
				_fail("concave triangle index length differs")
				return {}
			var first: Vector3 = points[triangle[0]]
			var second: Vector3 = points[triangle[1]]
			var third: Vector3 = points[triangle[2]]
			faces.append_array(PackedVector3Array([first, second, third]))
			var normal := (second - first).cross(third - first)
			if normal.length_squared() > MINIMUM_TRIANGLE_AREA_SQUARED:
				normal = normal.normalized() * RAY_MARGIN_GAME_UNITS * shape_scale
				var center := (first + second + third) / float(VECTOR_COMPONENTS)
				var candidate := PackedVector3Array([center + normal, center - normal])
				if query.is_empty():
					query = candidate
				probes.append(candidate[1].lerp(candidate[0], CONCAVE_PROBE_FRONT_FRACTION))
				probes.append(candidate[0].lerp(candidate[1], CONCAVE_PROBE_FRONT_FRACTION))
		if query.is_empty():
			_fail("concave collision has no nondegenerate query triangle")
			return {}
		var shape := ConcavePolygonShape3D.new()
		shape.set_faces(faces)
		return {
			"shape": shape,
			"localTransform": identity,
			"query": query,
			"probes": probes,
		}
	if shape_type == "SphereShape3D":
		var radius: float = contract["radiusGodotGameUnits"] * shape_scale
		var center := _vector(contract["centerGodotGameUnits"]) * shape_scale
		var shape := SphereShape3D.new()
		shape.radius = radius
		return {
			"shape": shape,
			"localTransform": Transform3D(Basis.IDENTITY, center),
			"query": PackedVector3Array([center, center + Vector3.RIGHT * (radius + RAY_MARGIN_GAME_UNITS * shape_scale)]),
			"probes": PackedVector3Array([center]),
		}
	if shape_type == "CapsuleShape3D":
		var first := _vector(contract["firstPointGodotGameUnits"]) * shape_scale
		var second := _vector(contract["secondPointGodotGameUnits"]) * shape_scale
		var radius: float = contract["radiusGodotGameUnits"] * shape_scale
		var axis := second - first
		if axis.is_zero_approx():
			_fail("capsule collision axis is zero")
			return {}
		var shape := CapsuleShape3D.new()
		shape.radius = radius
		shape.height = contract["heightGodotGameUnits"] * shape_scale
		var up := axis.normalized()
		var seed := Vector3.RIGHT if abs(up.dot(Vector3.RIGHT)) < 0.9 else Vector3.FORWARD
		var forward := seed.cross(up).normalized()
		var right := up.cross(forward).normalized()
		var center := (first + second) * 0.5
		return {
			"shape": shape,
			"localTransform": Transform3D(Basis(right, up, forward), center),
			"query": PackedVector3Array([center, center + right * (radius + RAY_MARGIN_GAME_UNITS * shape_scale)]),
			"probes": PackedVector3Array([center]),
		}
	_fail("unsupported Godot collision shape type: %s" % shape_type)
	return {}


func _inside_query(points: PackedVector3Array, shape_scale: float) -> PackedVector3Array:
	var center := Vector3.ZERO
	var bounds := AABB(points[0], Vector3.ZERO)
	for point in points:
		center += point
		bounds = bounds.expand(point)
	center /= float(points.size())
	var extent: float = maxf(bounds.size.x, maxf(bounds.size.y, bounds.size.z))
	return PackedVector3Array([center, center + Vector3.RIGHT * (extent + RAY_MARGIN_GAME_UNITS * shape_scale)])


func _placement_transform(placement: Dictionary, coordinates: Dictionary) -> Transform3D:
	var rotation_values: Array = placement["rotationGodotQuaternion"]
	var rotation := Quaternion(
		rotation_values[0],
		rotation_values[1],
		rotation_values[2],
		rotation_values[3],
	)
	return Transform3D(
		Basis(rotation),
		_vector(placement["positionGodotGameUnits"]) * coordinates["worldUnitsToMeters"],
	)


func _vectors(values: Array, scale: float) -> PackedVector3Array:
	var result := PackedVector3Array()
	for value in values:
		result.append(_vector(value) * scale)
	return result


func _vector(value: Array) -> Vector3:
	if value.size() != VECTOR_COMPONENTS:
		_fail("collision vector length differs")
		return Vector3.ZERO
	return Vector3(value[0], value[1], value[2])


func _read_json(path: String):
	if not FileAccess.file_exists(path):
		_fail("JSON input is absent: %s" % path)
		return null
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if parsed == null:
		_fail("JSON input is invalid: %s" % path)
	return parsed


func _fail(detail: String) -> void:
	printerr("TTW_FO3_STATIC_COLLISION_READINESS_FAIL %s" % detail)
	quit(2)
