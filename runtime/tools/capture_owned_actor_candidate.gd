extends SceneTree

# Private developer-only casting aid. It renders a legally owned compiled glTF
# without copying it into the repository, so classic-character analog choices can
# be reviewed from centered, repeatable full-body frames.

func _initialize() -> void:
	call_deferred("_capture")


func _capture() -> void:
	var options: Dictionary = _options(OS.get_cmdline_user_args())
	var model_path: String = String(options.get("model", ""))
	var output_path: String = String(options.get("output", ""))
	if model_path.is_empty() or output_path.is_empty():
		push_error("Use --model <owned actor.gltf> --output <frame.png> [--yaw <degrees>]")
		quit(2)
		return

	var document := GLTFDocument.new()
	var state := GLTFState.new()
	var load_error := document.append_from_file(model_path, state)
	if load_error != OK:
		push_error("Unable to load owned actor glTF: %s" % load_error)
		quit(2)
		return
	var actor := document.generate_scene(state)
	if actor == null:
		push_error("Owned actor glTF generated no scene")
		quit(2)
		return

	var viewport := SubViewport.new()
	viewport.name = "OwnedActorCandidateViewport"
	viewport.size = Vector2i(720, 960)
	viewport.own_world_3d = true
	viewport.transparent_bg = false
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	var world := Node3D.new()
	world.name = "OwnedActorCandidateCapture"
	viewport.add_child(world)
	world.add_child(actor)
	actor.rotation_degrees.y = float(String(options.get("yaw", "180")))

	var environment := WorldEnvironment.new()
	var environment_resource := Environment.new()
	environment_resource.background_mode = Environment.BG_COLOR
	environment_resource.background_color = Color("152019")
	environment_resource.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment_resource.ambient_light_color = Color("fff4df")
	environment_resource.ambient_light_energy = 0.82
	environment.environment = environment_resource
	world.add_child(environment)

	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-28.0, -32.0, 0.0)
	key.light_color = Color("fff0d5")
	key.light_energy = 1.35
	key.shadow_enabled = false
	world.add_child(key)
	var fill := DirectionalLight3D.new()
	fill.rotation_degrees = Vector3(-10.0, 145.0, 0.0)
	fill.light_color = Color("b8d7ff")
	fill.light_energy = 0.42
	fill.shadow_enabled = false
	world.add_child(fill)

	await process_frame
	var bounds := _visual_bounds(actor)
	if bounds.size.length_squared() <= 0.0:
		push_error("Owned actor glTF has no renderable bounds")
		quit(2)
		return
	print("OPENNV_OWNED_ACTOR_CANDIDATE_BOUNDS position=%s size=%s" % [bounds.position, bounds.size])
	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.keep_aspect = Camera3D.KEEP_HEIGHT
	camera.current = true
	camera.near = 0.02
	camera.far = max(50.0, bounds.size.z * 6.0)
	var center := bounds.get_center()
	var aspect := 720.0 / 960.0
	camera.size = max(bounds.size.y, bounds.size.x / aspect) * 1.14
	world.add_child(camera)
	camera.position = center + Vector3.BACK * max(4.0, bounds.size.z * 2.5)
	camera.look_at(center, Vector3.UP)

	for _frame in range(8):
		await process_frame
	RenderingServer.force_sync()
	var image := viewport.get_texture().get_image()
	var output_dir: String = output_path.get_base_dir()
	if not output_dir.is_empty():
		DirAccess.make_dir_recursive_absolute(output_dir)
	var save_error := image.save_png(output_path)
	if save_error != OK:
		push_error("Unable to save candidate frame: %s" % save_error)
		quit(2)
		return
	print("OPENNV_OWNED_ACTOR_CANDIDATE_CAPTURE %s" % output_path)
	quit(0)


func _visual_bounds(node: Node) -> AABB:
	var found := false
	var bounds := AABB()
	for child in _descendants(node):
		if child is MeshInstance3D and child.mesh != null:
			var child_bounds: AABB = child.global_transform * child.get_aabb()
			if not found:
				bounds = child_bounds
				found = true
			else:
				bounds = bounds.merge(child_bounds)
	return bounds if found else AABB()


func _descendants(node: Node) -> Array[Node]:
	var result: Array[Node] = []
	for child in node.get_children():
		result.append(child)
		result.append_array(_descendants(child))
	return result


func _options(arguments: PackedStringArray) -> Dictionary:
	var result: Dictionary = {}
	var index := 0
	while index < arguments.size():
		var key := arguments[index]
		if not key.begins_with("--") or index + 1 >= arguments.size():
			index += 1
			continue
		result[key.substr(2)] = arguments[index + 1]
		index += 2
	return result
