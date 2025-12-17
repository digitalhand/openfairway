class_name ShotTracker
extends Node3D

## Tracks shot statistics and manages ball tracers.
##
## Monitors the golf ball during flight and rollout to record
## apex height, carry distance, side distance, and total distance.

# Signals
signal good_data
signal bad_data
signal shot_complete(data: Dictionary)

# Tracer settings
@export var max_tracers: int = 4
@export var trail_resolution: float = 0.1

# Shot statistics
var apex := 0.0
var carry := 0.0
var side_distance := 0.0
var shot_data: Dictionary = {}

# Internal state
var _track_points := false
var _trail_timer := 0.0
var _tracers: Array[MeshInstance3D] = []
var _current_tracer: MeshInstance3D = null

@onready var _ball: GolfBall = $Ball


func _ready() -> void:
	max_tracers = GlobalSettings.range_settings.shot_tracer_count.value
	GlobalSettings.range_settings.shot_tracer_count.setting_changed.connect(_on_tracer_count_changed)


func _on_tracer_count_changed(value: int) -> void:
	max_tracers = value
	# Remove excess tracers if limit lowered
	while _tracers.size() > max_tracers:
		var oldest: MeshInstance3D = _tracers.pop_front()
		oldest.queue_free()


func _process(_delta: float) -> void:
	if Input.is_action_just_pressed("hit"):
		_start_shot()
		_ball.call_deferred("hit")

	if Input.is_action_just_pressed("reset"):
		reset_ball()


func _physics_process(delta: float) -> void:
	if not _track_points or _current_tracer == null:
		return

	apex = maxf(apex, _ball.position.y)
	side_distance = _ball.position.z

	if _ball.state == PhysicsEnums.BallState.FLIGHT:
		carry = _ball.get_downrange_yards() / 1.09361  # Convert to meters

	_trail_timer += delta
	if _trail_timer >= trail_resolution:
		_current_tracer.add_point(_ball.position)
		_trail_timer = 0.0


func _start_shot() -> void:
	_track_points = false
	apex = 0.0
	carry = 0.0
	side_distance = 0.0
	_create_new_tracer()

	if _current_tracer != null:
		_current_tracer.add_point(Vector3(0.0, 0.05, 0.0))

	_track_points = true
	_trail_timer = 0.0


func _create_new_tracer() -> MeshInstance3D:
	if max_tracers == 0:
		_current_tracer = null
		return null

	# Remove oldest if at limit
	if _tracers.size() >= max_tracers:
		var oldest: MeshInstance3D = _tracers.pop_front()
		oldest.queue_free()

	# Create new tracer
	var new_tracer := MeshInstance3D.new()
	new_tracer.set_script(preload("res://game/ball_trail.gd"))
	add_child(new_tracer)

	_tracers.append(new_tracer)
	_current_tracer = new_tracer
	return new_tracer


## Reset the ball and clear all tracers
func reset_ball() -> void:
	_ball.call_deferred("reset")
	_clear_all_tracers()
	apex = 0.0
	carry = 0.0
	side_distance = 0.0
	_reset_shot_data()


func _clear_all_tracers() -> void:
	for tracer in _tracers:
		tracer.queue_free()
	_tracers.clear()
	_current_tracer = null


func _reset_shot_data() -> void:
	for key in shot_data.keys():
		shot_data[key] = 0.0


## Get current total distance in meters
func get_distance() -> int:
	return int(_ball.get_downrange_yards() / 1.09361)


## Get current side distance in meters
func get_side_distance() -> int:
	return int(_ball.position.z)


## Get current ball state
func get_ball_state() -> PhysicsEnums.BallState:
	return _ball.state


## Validate incoming shot data
func validate_data(data: Dictionary) -> bool:
	# TODO: Implement proper validation
	return not data.is_empty()


func _on_ball_rest() -> void:
	_track_points = false
	shot_data["TotalDistance"] = int(_ball.get_downrange_yards() / 1.09361)
	shot_data["CarryDistance"] = int(carry)
	shot_data["Apex"] = int(apex)
	shot_data["SideDistance"] = int(side_distance)
	shot_complete.emit(shot_data)


## Handle incoming shot from TCP (launch monitor)
func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	if not validate_data(data):
		bad_data.emit()
		return

	good_data.emit()
	shot_data = data.duplicate()
	_start_shot()
	_ball.call_deferred("hit_from_data", data)


## Handle locally injected shot from UI
func _on_range_ui_hit_shot(data: Variant) -> void:
	shot_data = (data as Dictionary).duplicate()
	print("Local shot injection payload: ", JSON.stringify(shot_data))
	_start_shot()
	_ball.call_deferred("hit_from_data", data)


func _on_range_ui_set_env(data: Variant) -> void:
	_ball.call_deferred("set_env", data)
