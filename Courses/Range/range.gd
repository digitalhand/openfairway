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

@onready var _shot_tracker: ShotTracker = $ShotTracker
@onready var _range_ui = $RangeUI


func _ready() -> void:
	var settings := GlobalSettings.range_settings
	settings.camera_follow_mode.setting_changed.connect(_on_camera_follow_changed)
	settings.surface_type.setting_changed.connect(_on_surface_changed)

	_on_camera_follow_changed(settings.camera_follow_mode.value)
	_apply_surface_to_ball()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("reset"):
		_reset_display_data()
		_range_ui.set_data(display_data)


func _process(_delta: float) -> void:
	# Update UI during flight/rollout
	if _shot_tracker.get_ball_state() != PhysicsEnums.BallState.REST:
		_update_ball_display()


func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	raw_ball_data = data.duplicate()
	_update_ball_display()

	if GlobalSettings.range_settings.camera_follow_mode.value:
		_on_camera_follow_changed(true)


func _on_golf_ball_rest(_ball_data: Dictionary) -> void:
	raw_ball_data = _ball_data.duplicate()
	_update_ball_display()

	var settings := GlobalSettings.range_settings

	# Reset camera after delay if follow mode enabled
	if settings.camera_follow_mode.value:
		var delay: float = settings.ball_reset_timer.value
		await get_tree().create_timer(delay).timeout
		_reset_camera_to_start()

	# Auto-reset ball if enabled
	if settings.auto_ball_reset.value:
		await get_tree().create_timer(settings.ball_reset_timer.value).timeout
		_reset_display_data()
		_range_ui.set_data(display_data)
		_shot_tracker.reset_ball()


func _on_range_ui_hit_shot(data: Dictionary) -> void:
	raw_ball_data = data.duplicate()
	_update_ball_display()

	if GlobalSettings.range_settings.camera_follow_mode.value:
		_on_camera_follow_changed(true)


func _on_camera_follow_changed(value: bool) -> void:
	if value:
		$PhantomCamera3D.follow_mode = 1  # FRAMED
		$PhantomCamera3D.follow_target = $ShotTracker/Ball
	else:
		$PhantomCamera3D.follow_mode = 0  # NONE


func _reset_camera_to_start() -> void:
	$PhantomCamera3D.follow_mode = 0  # NONE

	var start_pos := Vector3(-2.5, 1.5, 0)
	var tween := create_tween()
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.tween_property($PhantomCamera3D, "global_position", start_pos, 1.5)

	await tween.finished

	# Reset ball position for next shot visibility
	$ShotTracker/Ball.position = Vector3(0.0, 0.05, 0.0)
	$ShotTracker/Ball.velocity = Vector3.ZERO
	$ShotTracker/Ball.omega = Vector3.ZERO
	$ShotTracker/Ball.state = PhysicsEnums.BallState.REST


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
	display_data["VLA"] = 0.0
	display_data["HLA"] = 0.0
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
