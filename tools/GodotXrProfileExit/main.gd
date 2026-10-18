extends Node3D

var frames := 0

func _ready():
	var action_map: OpenXRActionMap = load("res://openxr_action_map.tres")
	var profiles = action_map.get_interaction_profiles()
	var touch_profiles := 0
	for profile in profiles:
		if profile.interaction_profile_path == "/interaction_profiles/oculus/touch_controller":
			touch_profiles += 1
	print("OPENNV_XR_PROFILE_FIXTURE_MAP profileResources=%d touchProfiles=%d" % [profiles.size(), touch_profiles])
	var xr = XRServer.find_interface("OpenXR")
	if xr == null or not xr.is_initialized():
		push_error("OPENNV_XR_PROFILE_FIXTURE_NO_OPENXR")
		get_tree().quit(1)
		return
	get_viewport().use_xr = true
	var origin = XROrigin3D.new()
	add_child(origin)
	var camera = XRCamera3D.new()
	origin.add_child(camera)
	camera.current = true
	var mesh = MeshInstance3D.new()
	mesh.mesh = BoxMesh.new()
	mesh.position = Vector3(0, 1.4, -1.5)
	var material = StandardMaterial3D.new()
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.albedo_color = Color(0.2, 0.4, 0.8)
	mesh.material_override = material
	add_child(mesh)

func _process(_delta):
	frames += 1
	if frames == 45:
		print("OPENNV_XR_PROFILE_FIXTURE_DONE processFrames=45 applicationTeardown=false ownedData=false recording=false")
		get_tree().quit()
