class_name GolfBall
extends CharacterBody3D

## Physics simulation for a golf ball in flight and on ground.
##
## Implements aerodynamic forces (drag, Magnus lift), spin decay,
## ground friction, and bounce physics based on golf ball research.

# Ball physical properties
const MASS := 0.04592623  ## kg (regulation golf ball)
const RADIUS := 0.021335  ## m (regulation golf ball)
const CROSS_SECTION := PI * RADIUS * RADIUS  ## m²
const MOMENT_OF_INERTIA := 0.4 * MASS * RADIUS * RADIUS  ## kg*m²

# Spin decay time constant (seconds) - tuned to match realistic behavior
const SPIN_DECAY_TAU := 3.0

# Signals
signal ball_at_rest

# State
var state: PhysicsEnums.BallState = PhysicsEnums.BallState.REST
var omega := Vector3.ZERO  ## Angular velocity (rad/s)
var on_ground := false
var floor_normal := Vector3.UP

# Surface parameters (set via set_surface)
var surface_type: PhysicsEnums.Surface = PhysicsEnums.Surface.FAIRWAY
var _kinetic_friction := 0.42
var _rolling_friction := 0.18
var _grass_viscosity := 0.0020
var _critical_angle := 0.30  ## radians

# Environment
var _air_density: float
var _air_viscosity: float
var _drag_scale := 1.0
var _lift_scale := 1.0

# Shot tracking
var shot_start_pos := Vector3.ZERO
var shot_direction := Vector3(1.0, 0.0, 0.0)  ## Normalized horizontal direction
var launch_spin_rpm := 0.0  ## Stored for bounce calculations


func _ready() -> void:
	_connect_settings()
	_update_environment()
	_apply_surface_params()


func _connect_settings() -> void:
	var settings := GlobalSettings.range_settings
	settings.temperature.setting_changed.connect(_on_environment_changed)
	settings.altitude.setting_changed.connect(_on_environment_changed)
	settings.range_units.setting_changed.connect(_on_environment_changed)
	settings.drag_scale.setting_changed.connect(_on_drag_scale_changed)
	settings.lift_scale.setting_changed.connect(_on_lift_scale_changed)
	_drag_scale = settings.drag_scale.value
	_lift_scale = settings.lift_scale.value


func _update_environment() -> void:
	var settings := GlobalSettings.range_settings
	var units: PhysicsEnums.Units = settings.range_units.value as PhysicsEnums.Units
	_air_density = Aerodynamics.get_air_density(
		settings.altitude.value,
		settings.temperature.value,
		units
	)
	_air_viscosity = Aerodynamics.get_dynamic_viscosity(
		settings.temperature.value,
		units
	)


func _on_environment_changed(_value) -> void:
	_update_environment()


func _on_drag_scale_changed(_value) -> void:
	_drag_scale = GlobalSettings.range_settings.drag_scale.value


func _on_lift_scale_changed(_value) -> void:
	_lift_scale = GlobalSettings.range_settings.lift_scale.value


## Set the surface type and update friction parameters
func set_surface(surface: PhysicsEnums.Surface) -> void:
	surface_type = surface
	_apply_surface_params()


func _apply_surface_params() -> void:
	var params := Surface.get_params(surface_type)
	_kinetic_friction = params["u_k"]
	_rolling_friction = params["u_kr"]
	_grass_viscosity = params["nu_g"]
	_critical_angle = params["theta_c"]


## Get downrange distance in yards (along initial shot direction)
func get_downrange_yards() -> float:
	var delta: Vector3 = position - shot_start_pos
	var meters: float = delta.dot(shot_direction)
	return meters * 1.09361


func _physics_process(delta: float) -> void:
	if state == PhysicsEnums.BallState.REST:
		return

	var was_on_ground := on_ground
	var prev_velocity := velocity

	# Calculate forces
	var total_force := _calculate_forces(was_on_ground)
	var total_torque := _calculate_torques(was_on_ground)

	# Update velocity and angular velocity
	velocity += (total_force / MASS) * delta
	omega += (total_torque / MOMENT_OF_INERTIA) * delta

	# Safety bounds check
	if _check_out_of_bounds():
		return

	# Move and handle collisions
	var collision := move_and_collide(velocity * delta)
	_handle_collision(collision, was_on_ground, prev_velocity)

	# Check for rest
	if velocity.length() < 0.1 and state != PhysicsEnums.BallState.REST:
		_enter_rest_state()


func _calculate_forces(was_on_ground: bool) -> Vector3:
	var gravity := Vector3(0.0, -9.81 * MASS, 0.0)

	if was_on_ground:
		return gravity + _calculate_ground_forces()
	else:
		return gravity + _calculate_air_forces()


func _calculate_ground_forces() -> Vector3:
	# Grass drag
	var grass_drag := velocity * (-6.0 * PI * RADIUS * _grass_viscosity)
	grass_drag.y = 0.0

	# Contact point velocity for friction calculation
	var contact_velocity := velocity + omega.cross(-floor_normal * RADIUS)
	var tangent_velocity := contact_velocity - floor_normal * contact_velocity.dot(floor_normal)

	var friction := Vector3.ZERO
	if tangent_velocity.length() < 0.05:
		# Rolling without slipping
		var flat_velocity := velocity - floor_normal * velocity.dot(floor_normal)
		var friction_dir := flat_velocity.normalized() if flat_velocity.length() > 0.01 else Vector3.ZERO
		friction = friction_dir * (-_rolling_friction * MASS * 9.81)
	else:
		# Slipping - kinetic friction
		var slip_dir := tangent_velocity.normalized()
		friction = slip_dir * (-_kinetic_friction * MASS * 9.81)

	return grass_drag + friction


func _calculate_air_forces() -> Vector3:
	var speed := velocity.length()
	if speed < 0.5:
		return Vector3.ZERO

	var spin_ratio := omega.length() * RADIUS / speed
	var reynolds := _air_density * speed * RADIUS * 2.0 / _air_viscosity

	var cd := Aerodynamics.get_cd(reynolds) * _drag_scale
	var cl := Aerodynamics.get_cl(reynolds, spin_ratio) * _lift_scale

	# Drag force (opposite to velocity)
	var drag := -0.5 * cd * _air_density * CROSS_SECTION * velocity * speed

	# Magnus force (perpendicular to velocity and spin axis)
	var magnus := Vector3.ZERO
	var omega_len := omega.length()
	if omega_len > 0.1:
		var omega_cross_vel := omega.cross(velocity)
		magnus = 0.5 * cl * _air_density * CROSS_SECTION * omega_cross_vel * speed / omega_len

	return drag + magnus


func _calculate_torques(was_on_ground: bool) -> Vector3:
	if was_on_ground:
		return _calculate_ground_torques()
	else:
		# Spin decay torque (exponential decay model)
		return -MOMENT_OF_INERTIA * omega / SPIN_DECAY_TAU


func _calculate_ground_torques() -> Vector3:
	var friction_torque := Vector3.ZERO
	var grass_torque := -6.0 * PI * _grass_viscosity * RADIUS * omega

	# Calculate friction for torque
	var contact_velocity := velocity + omega.cross(-floor_normal * RADIUS)
	var tangent_velocity := contact_velocity - floor_normal * contact_velocity.dot(floor_normal)

	var friction_force := Vector3.ZERO
	if tangent_velocity.length() < 0.05:
		var flat_velocity := velocity - floor_normal * velocity.dot(floor_normal)
		var friction_dir := flat_velocity.normalized() if flat_velocity.length() > 0.01 else Vector3.ZERO
		friction_force = friction_dir * (-_rolling_friction * MASS * 9.81)
	else:
		var slip_dir := tangent_velocity.normalized()
		friction_force = slip_dir * (-_kinetic_friction * MASS * 9.81)

	if friction_force.length() > 0.001:
		friction_torque = (-floor_normal * RADIUS).cross(friction_force)

	return friction_torque + grass_torque


func _check_out_of_bounds() -> bool:
	if absf(position.x) > 1000.0 or absf(position.z) > 1000.0:
		print("WARNING: Ball out of bounds at: ", position)
		_enter_rest_state()
		return true

	if position.y < -0.5:
		print("WARNING: Ball fell through ground at: ", position)
		position.y = 0.0
		_enter_rest_state()
		return true

	return false


func _handle_collision(collision: KinematicCollision3D, was_on_ground: bool, prev_velocity: Vector3) -> void:
	if collision:
		var normal := collision.get_normal()

		if _is_ground_normal(normal):
			floor_normal = normal
			var is_landing := (state == PhysicsEnums.BallState.FLIGHT) or prev_velocity.y < -0.5

			if is_landing:
				if state == PhysicsEnums.BallState.FLIGHT:
					_print_impact_debug()
				velocity = _calculate_bounce(velocity, normal)
				print("  Velocity after bounce: ", velocity, " (%.2f m/s)" % velocity.length())
				on_ground = false
			else:
				on_ground = true
				if velocity.y < 0:
					velocity.y = 0
		else:
			# Wall collision - damped reflection
			on_ground = false
			floor_normal = Vector3.UP
			velocity = velocity.bounce(normal) * 0.30
	else:
		# No collision - check rolling continuity
		if state != PhysicsEnums.BallState.FLIGHT and was_on_ground and position.y < 0.02 and velocity.y <= 0.0:
			on_ground = true
		else:
			on_ground = false
			floor_normal = Vector3.UP


func _is_ground_normal(normal: Vector3) -> bool:
	return normal.y > 0.7


func _calculate_bounce(vel: Vector3, normal: Vector3) -> Vector3:
	if state == PhysicsEnums.BallState.FLIGHT:
		state = PhysicsEnums.BallState.ROLLOUT

	# Decompose velocity
	var vel_normal := vel.project(normal)
	var speed_normal := vel_normal.length()
	var vel_tangent := vel - vel_normal
	var speed_tangent := vel_tangent.length()

	# Decompose angular velocity
	var omega_normal := omega.project(normal)
	var omega_tangent := omega - omega_normal

	var impact_angle := vel.angle_to(normal)
	var spin_component: float = omega.dot(normal)

	# Tangential retention based on spin
	var current_spin_rpm := omega.length() / 0.10472
	var spin_factor := clampf(1.0 - (current_spin_rpm / 8000.0), 0.40, 1.0)
	var tangential_retention := 0.55 * spin_factor

	if state == PhysicsEnums.BallState.ROLLOUT:
		print("  Bounce: spin=%.0f rpm, factor=%.3f, retention=%.3f" % [
			current_spin_rpm, spin_factor, tangential_retention
		])

	# Calculate new tangential speed
	var new_tangent_speed := tangential_retention * vel.length() * sin(impact_angle - _critical_angle) - \
		2.0 * RADIUS * absf(spin_component) / 7.0

	if speed_tangent < 0.01 or new_tangent_speed <= 0.0:
		vel_tangent = Vector3.ZERO
	else:
		vel_tangent = vel_tangent.limit_length(new_tangent_speed)

	# Update tangential angular velocity
	var new_omega_tangent := new_tangent_speed / RADIUS
	if omega_tangent.length() < 0.1 or new_omega_tangent <= 0.0:
		omega_tangent = Vector3.ZERO
	else:
		omega_tangent = omega_tangent.limit_length(new_omega_tangent)

	# Coefficient of restitution (speed-dependent)
	var cor := _get_coefficient_of_restitution(speed_normal)
	vel_normal = vel_normal * -cor

	omega = omega_normal + omega_tangent

	return vel_normal + vel_tangent


func _get_coefficient_of_restitution(speed_normal: float) -> float:
	if speed_normal > 20.0:
		return 0.25  # High speed impacts
	elif speed_normal < 2.0:
		return 0.0  # Kill very small bounces
	else:
		# Typical COR curve for golf ball on turf
		return 0.45 - 0.0100 * speed_normal + 0.0002 * speed_normal * speed_normal


func _print_impact_debug() -> void:
	print("FIRST IMPACT at pos: ", position, ", downrange: %.2f yds" % get_downrange_yards())
	print("  Velocity at impact: ", velocity, " (%.2f m/s)" % velocity.length())
	print("  Spin at impact: ", omega, " (%.0f rpm)" % (omega.length() / 0.10472))
	print("  Normal: ", floor_normal)


func _enter_rest_state() -> void:
	state = PhysicsEnums.BallState.REST
	velocity = Vector3.ZERO
	omega = Vector3.ZERO
	ball_at_rest.emit()


## Reset ball to starting position
func reset() -> void:
	position = Vector3(0.0, 0.1, 0.0)
	velocity = Vector3.ZERO
	omega = Vector3.ZERO
	launch_spin_rpm = 0.0
	state = PhysicsEnums.BallState.REST
	on_ground = false


## Hit ball with default test data
func hit() -> void:
	var data := {
		"Speed": 100.0,
		"VLA": 22.0,
		"HLA": -3.1,
		"TotalSpin": 6000.0,
		"SpinAxis": 3.5,
	}
	hit_from_data(data)


## Hit ball with provided launch data
func hit_from_data(data: Dictionary) -> void:
	var speed_mps: float = float(data.get("Speed", 0.0)) * 0.44704  # mph to m/s
	var vla_deg: float = float(data.get("VLA", 0.0))
	var hla_deg: float = float(data.get("HLA", 0.0))

	# Parse spin data (handle both backspin/sidespin and totalspin/axis formats)
	var spin_data := _parse_spin_data(data)
	var total_spin: float = spin_data.total
	var spin_axis: float = spin_data.axis

	# Set state
	state = PhysicsEnums.BallState.FLIGHT
	on_ground = false
	position = Vector3(0.0, 0.05, 0.0)

	# Calculate initial velocity
	velocity = Vector3(speed_mps, 0, 0) \
		.rotated(Vector3.FORWARD, deg_to_rad(vla_deg)) \
		.rotated(Vector3.UP, deg_to_rad(-hla_deg))

	# Set shot tracking
	shot_start_pos = position
	var flat_velocity := Vector3(velocity.x, 0.0, velocity.z)
	shot_direction = flat_velocity.normalized() if flat_velocity.length() > 0.001 else Vector3.RIGHT

	# Set angular velocity
	omega = Vector3(0.0, 0.0, total_spin * 0.10472) \
		.rotated(Vector3.RIGHT, deg_to_rad(spin_axis))
	launch_spin_rpm = total_spin

	_print_launch_debug(data, speed_mps, vla_deg, hla_deg, total_spin, spin_axis)


func _parse_spin_data(data: Dictionary) -> Dictionary:
	var has_backspin := data.has("BackSpin")
	var has_sidespin := data.has("SideSpin")
	var has_total := data.has("TotalSpin")
	var has_axis := data.has("SpinAxis")

	var backspin: float = float(data.get("BackSpin", 0.0))
	var sidespin: float = float(data.get("SideSpin", 0.0))
	var total_spin: float = float(data.get("TotalSpin", 0.0))
	var spin_axis: float = float(data.get("SpinAxis", 0.0))

	# Calculate missing values
	if total_spin == 0.0 and (has_backspin or has_sidespin):
		total_spin = sqrt(backspin * backspin + sidespin * sidespin)

	if not has_axis and (has_backspin or has_sidespin):
		spin_axis = rad_to_deg(atan2(sidespin, backspin))

	if has_total and has_axis:
		if not has_backspin:
			backspin = total_spin * cos(deg_to_rad(spin_axis))
		if not has_sidespin:
			sidespin = total_spin * sin(deg_to_rad(spin_axis))

	return {"total": total_spin, "axis": spin_axis, "back": backspin, "side": sidespin}


func _print_launch_debug(data: Dictionary, speed_mps: float, vla: float, hla: float, spin: float, axis: float) -> void:
	print("=== SHOT DEBUG ===")
	print("Speed: %.2f mph (%.2f m/s)" % [data.get("Speed", 0.0), speed_mps])
	print("VLA: %.2f°, HLA: %.2f°" % [vla, hla])
	print("Spin: %.0f rpm, Axis: %.2f°" % [spin, axis])
	print("drag_cf: %.2f, lift_cf: %.2f" % [_drag_scale, _lift_scale])
	print("Air density: %.4f kg/m³" % _air_density)
	print("Dynamic viscosity: %.11f" % _air_viscosity)

	var Re_initial := _air_density * speed_mps * RADIUS * 2.0 / _air_viscosity
	var spin_ratio := (spin * 0.10472) * RADIUS / speed_mps if speed_mps > 0.1 else 0.0
	var Cl_initial := Aerodynamics.get_cl(Re_initial, spin_ratio)
	print("Reynolds number: %.0f" % Re_initial)
	print("Spin ratio: %.3f" % spin_ratio)
	print("Cl (before scale): %.3f, after: %.3f" % [Cl_initial, Cl_initial * _lift_scale])
	print("Initial velocity: ", velocity)
	print("Initial omega: ", omega, " (%.0f rpm)" % (omega.length() / 0.10472))
	print("Shot direction: ", shot_direction)
	print("===================")
