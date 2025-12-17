class_name BallTrail
extends MeshInstance3D

## Visual trail/tracer for golf ball flight path.
##
## Creates a camera-facing ribbon mesh that follows the ball's trajectory.

@export var color := Color(0.6, 0.0, 0.0, 1.0)  ## Trail color (darker red default)
@export var line_width := 0.08  ## Width of the trail ribbon
@export var smoothing_step: float = 0.05  ## Meters between baked samples for smoother ribbons

var _points: PackedVector3Array = []
var _material: StandardMaterial3D


func _ready() -> void:
	mesh = ArrayMesh.new()
	cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_setup_material()


func _setup_material() -> void:
	_material = StandardMaterial3D.new()
	_material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_material.albedo_color = color
	_material.emission_enabled = true
	_material.emission = color
	_material.emission_energy_multiplier = 0.5
	_material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	_material.blend_mode = BaseMaterial3D.BLEND_MODE_MIX
	_material.disable_receive_shadows = true
	_material.no_depth_test = false


func _process(_delta: float) -> void:
	_draw_trail()


## Set the trail color
func set_color(new_color: Color) -> void:
	color = new_color
	if _material:
		_material.albedo_color = color
		_material.emission = color


## Add a point to the trail
func add_point(point: Vector3) -> void:
	_points.append(point)


## Clear all points from the trail
func clear_points() -> void:
	_points.clear()
	if mesh:
		(mesh as ArrayMesh).clear_surfaces()


func _draw_trail() -> void:
	var array_mesh := mesh as ArrayMesh
	array_mesh.clear_surfaces()

	if _points.size() < 2:
		return

	_create_ribbon_mesh(array_mesh)


func _create_ribbon_mesh(array_mesh: ArrayMesh) -> void:
	var vertices := PackedVector3Array()
	var uvs := PackedVector2Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()

	var camera := get_viewport().get_camera_3d()
	if camera == null:
		return

	var render_points := _get_smoothed_points(_points, smoothing_step)

	for i in range(render_points.size()):
		var point: Vector3 = render_points[i]

		# Billboard direction to camera
		var to_camera := (camera.global_position - point).normalized()

		# Forward direction along the path
		var forward := _get_forward_direction(render_points, i)

		# Perpendicular vector for ribbon width
		var right := to_camera.cross(forward).normalized()
		if right.length() < 0.01:
			right = Vector3.RIGHT

		# Fade out towards the end
		var alpha := _calculate_alpha(render_points.size(), i)

		# Create two vertices (left and right of center)
		var half_width := line_width * 0.5
		vertices.append(point - right * half_width)
		vertices.append(point + right * half_width)

		# UVs
		var t := float(i) / float(render_points.size() - 1)
		uvs.append(Vector2(0, t))
		uvs.append(Vector2(1, t))

		# Colors with alpha
		var vertex_color := Color(color.r, color.g, color.b, alpha)
		colors.append(vertex_color)
		colors.append(vertex_color)

		# Triangle indices (connecting to previous segment)
		if i > 0:
			var base := i * 2
			# First triangle
			indices.append(base)
			indices.append(base - 2)
			indices.append(base - 1)
			# Second triangle
			indices.append(base - 1)
			indices.append(base + 1)
			indices.append(base)

	# Build the mesh
	var arrays: Array = []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = indices

	array_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	array_mesh.surface_set_material(0, _material)


func _get_forward_direction(points: PackedVector3Array, index: int) -> Vector3:
	if index < points.size() - 1:
		return (points[index + 1] - points[index]).normalized()
	elif index > 0:
		return (points[index] - points[index - 1]).normalized()
	else:
		return Vector3.FORWARD


func _calculate_alpha(point_count: int, index: int) -> float:
	var points_from_end := point_count - 1 - index
	if points_from_end < 3:
		return float(points_from_end + 1) / 4.0
	return 1.0


func _get_smoothed_points(points: PackedVector3Array, step: float) -> PackedVector3Array:
	if points.size() < 2 or step <= 0.0:
		return points

	var curve := Curve3D.new()
	for p in points:
		curve.add_point(p)

	curve.bake_interval = step
	var total_length := curve.get_baked_length()

	var smoothed := PackedVector3Array()
	var d := 0.0
	while d < total_length:
		smoothed.append(curve.sample_baked(d))
		d += step

	# Ensure final point included
	smoothed.append(points[points.size() - 1])
	return smoothed
