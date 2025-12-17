extends Node3D

## Main range scene controller.
##
## Manages the connection between TCP server, shot tracker, and UI.

var display_data: Dictionary = {
	"Distance": "---",
	"Carry": "---",
	"Offline": "---",
	"Apex": "---",
	"VLA": 0.0,
	"HLA": 0.0,
	"Speed": "---",
	"BackSpin": "---",
	"SideSpin": "---",
	"TotalSpin": "---",
	"SpinAxis": "---"
}

var raw_ball_data: Dictionary = {}
var last_display: Dictionary = {}

const CAMERA_FOLLOW_BACK := 8.0
const CAMERA_FOLLOW_HEIGHT := 2.0
const CAMERA_START_POS := Vector3(-2.5, 1.5, 0.0)
const CAMERA_LOOK_OFFSET := Vector3(0.0, 1.5, 0.0)

@onready var _shot_tracker: ShotTracker = $ShotTracker
@onready var _range_ui = $RangeUI


func _ready() -> void:
	var settings := GlobalSettings.range_settings
	settings.camera_follow_mode.setting_changed.connect(_on_camera_follow_changed)
	settings.surface_type.setting_changed.connect(_on_surface_changed)

	_set_camera_to_start_immediate()
	_on_camera_follow_changed(settings.camera_follow_mode.value)
	_apply_surface_to_ball()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("reset"):
		_reset_display_data()
		_range_ui.set_data(display_data)
		_set_camera_to_start_immediate()


func _process(_delta: float) -> void:
	# Update UI during flight/rollout
	if _shot_tracker.get_ball_state() != PhysicsEnums.BallState.REST:
		_update_ball_display()


func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	raw_ball_data = data.duplicate()
	_update_ball_display()

	# Enable camera follow when shot is hit
	_on_camera_follow_changed(true)


func _on_golf_ball_rest(_ball_data: Dictionary) -> void:
	raw_ball_data = _ball_data.duplicate()
	_update_ball_display()

	var settings := GlobalSettings.range_settings

	# Freeze camera at its current spot on rest to avoid drift/overshoot
	_freeze_camera_on_ball()

	# Reset camera after delay
	var delay: float = settings.ball_reset_timer.value
	await get_tree().create_timer(delay).timeout
	_reset_camera_to_start()

	# Auto-reset ball if enabled
	if settings.auto_ball_reset.value:
		_reset_display_data()
		_range_ui.set_data(display_data)
		_shot_tracker.reset_ball()


func _on_range_ui_hit_shot(data: Dictionary) -> void:
	raw_ball_data = data.duplicate()
	_update_ball_display()

	# Enable camera follow when shot is hit
	_on_camera_follow_changed(true)


func _on_camera_follow_changed(value: bool) -> void:
	if value:
		_start_camera_follow()
	else:
		$PhantomCamera3D.follow_mode = PhantomCamera3D.FollowMode.NONE


func _reset_camera_to_start() -> void:
	$PhantomCamera3D.follow_mode = PhantomCamera3D.FollowMode.NONE

	var start_pos := CAMERA_START_POS
	var tween := create_tween()
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.tween_property($PhantomCamera3D, "global_position", start_pos, 1.5)

	await tween.finished

	# Reset ball position for next shot visibility
	$ShotTracker/Ball.position = Vector3(0.0, GolfBall.START_HEIGHT, 0.0)
	$ShotTracker/Ball.velocity = Vector3.ZERO
	$ShotTracker/Ball.omega = Vector3.ZERO
	$ShotTracker/Ball.state = PhysicsEnums.BallState.REST
	$PhantomCamera3D.look_at($ShotTracker/Ball.global_position + CAMERA_LOOK_OFFSET, Vector3.UP)
	_sync_main_camera_to_phantom()


func _start_camera_follow() -> void:
	var cam := $PhantomCamera3D
	cam.follow_mode = PhantomCamera3D.FollowMode.SIMPLE
	cam.follow_target = $ShotTracker/Ball
	cam.follow_offset = _compute_follow_offset()
	cam.follow_damping = true
	cam.look_at_mode = PhantomCamera3D.LookAtMode.SIMPLE
	cam.look_at_target = $ShotTracker/Ball


func _compute_follow_offset() -> Vector3:
	var ball := $ShotTracker/Ball
	var dir: Vector3 = ball.velocity
	if dir.length() < 0.5:
		dir = ball.shot_direction
	dir = dir.normalized()

	var back := -dir * CAMERA_FOLLOW_BACK
	var up := Vector3.UP * CAMERA_FOLLOW_HEIGHT
	return back + up


func _freeze_camera_on_ball() -> void:
	var cam := $PhantomCamera3D
	cam.follow_mode = PhantomCamera3D.FollowMode.NONE
	cam.look_at_mode = PhantomCamera3D.LookAtMode.NONE


func _set_camera_to_start_immediate() -> void:
	var cam := $PhantomCamera3D
	cam.follow_mode = PhantomCamera3D.FollowMode.NONE
	cam.look_at_mode = PhantomCamera3D.LookAtMode.NONE
	cam.global_position = CAMERA_START_POS
	# Point the camera toward the ball start position
	cam.look_at($ShotTracker/Ball.global_position + CAMERA_LOOK_OFFSET, Vector3.UP)
	_sync_main_camera_to_phantom()


func _sync_main_camera_to_phantom() -> void:
	var phantom := $PhantomCamera3D
	$Camera3D.global_transform = phantom.global_transform


func _on_surface_changed(_value: PhysicsEnums.Surface) -> void:
	_apply_surface_to_ball()


func _apply_surface_to_ball() -> void:
	if _shot_tracker and _shot_tracker.has_node("Ball"):
		_shot_tracker.get_node("Ball").set_surface(
			GlobalSettings.range_settings.surface_type.value as PhysicsEnums.Surface
		)


func _reset_display_data() -> void:
	raw_ball_data.clear()
	last_display.clear()
	display_data["Distance"] = "---"
	display_data["Carry"] = "---"
	display_data["Offline"] = "---"
	display_data["Apex"] = "---"
	display_data["VLA"] = "---"
	display_data["HLA"] = "---"
	display_data["Speed"] = "---"
	display_data["BackSpin"] = "---"
	display_data["SideSpin"] = "---"
	display_data["TotalSpin"] = "---"
	display_data["SpinAxis"] = "---"


func _update_ball_display() -> void:
	var show_distance := true
	display_data = ShotFormatter.format_ball_display(
		raw_ball_data,
		_shot_tracker,
		GlobalSettings.range_settings.range_units.value as PhysicsEnums.Units,
		show_distance,
		display_data
	)
	last_display = display_data.duplicate()
	_range_ui.set_data(display_data)
