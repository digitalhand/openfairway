class_name BallPhysics
extends RefCounted

## Pure physics calculations for golf ball motion.
##
## Contains all force, torque, and bounce calculations separated from
## the game object (CharacterBody3D) implementation.

# Ball physical properties
const MASS := 0.04592623  ## kg (regulation golf ball)
const RADIUS := 0.021335  ## m (regulation golf ball)
const CROSS_SECTION := PI * RADIUS * RADIUS  ## m²
const MOMENT_OF_INERTIA := 0.4 * MASS * RADIUS * RADIUS  ## kg*m²
const SPIN_DECAY_TAU := 3.0  ## Spin decay time constant (seconds)


## Physics parameters structure
class PhysicsParams:
	var air_density: float
	var air_viscosity: float
	var drag_scale: float
	var lift_scale: float
	var kinetic_friction: float
	var rolling_friction: float
	var grass_viscosity: float
	var critical_angle: float
	var floor_normal: Vector3

	func _init(
		p_air_density: float,
		p_air_viscosity: float,
		p_drag_scale: float,
		p_lift_scale: float,
		p_kinetic_friction: float,
		p_rolling_friction: float,
		p_grass_viscosity: float,
		p_critical_angle: float,
		p_floor_normal: Vector3
	) -> void:
		air_density = p_air_density
		air_viscosity = p_air_viscosity
		drag_scale = p_drag_scale
		lift_scale = p_lift_scale
		kinetic_friction = p_kinetic_friction
		rolling_friction = p_rolling_friction
		grass_viscosity = p_grass_viscosity
		critical_angle = p_critical_angle
		floor_normal = p_floor_normal


## Calculate total forces acting on the ball
static func calculate_forces(
	velocity: Vector3,
	omega: Vector3,
	on_ground: bool,
	params: PhysicsParams
) -> Vector3:
	var gravity := Vector3(0.0, -9.81 * MASS, 0.0)

	if on_ground:
		return gravity + calculate_ground_forces(velocity, omega, params)
	else:
		return gravity + calculate_air_forces(velocity, omega, params)


## Calculate ground friction and drag forces
static func calculate_ground_forces(
	velocity: Vector3,
	omega: Vector3,
	params: PhysicsParams
) -> Vector3:
	# Grass drag
	var grass_drag := velocity * (-6.0 * PI * RADIUS * params.grass_viscosity)
	grass_drag.y = 0.0

	# Contact point velocity for friction calculation
	var contact_velocity := velocity + omega.cross(-params.floor_normal * RADIUS)
	var tangent_velocity := contact_velocity - params.floor_normal * contact_velocity.dot(params.floor_normal)

	var friction := Vector3.ZERO
	if tangent_velocity.length() < 0.05:
		# Rolling without slipping
		var flat_velocity := velocity - params.floor_normal * velocity.dot(params.floor_normal)
		var friction_dir := flat_velocity.normalized() if flat_velocity.length() > 0.01 else Vector3.ZERO
		friction = friction_dir * (-params.rolling_friction * MASS * 9.81)
	else:
		# Slipping - kinetic friction
		var slip_dir := tangent_velocity.normalized()
		friction = slip_dir * (-params.kinetic_friction * MASS * 9.81)

	return grass_drag + friction


## Calculate aerodynamic drag and Magnus forces
static func calculate_air_forces(
	velocity: Vector3,
	omega: Vector3,
	params: PhysicsParams
) -> Vector3:
	var speed := velocity.length()
	if speed < 0.5:
		return Vector3.ZERO

	var spin_ratio := omega.length() * RADIUS / speed
	var reynolds := params.air_density * speed * RADIUS * 2.0 / params.air_viscosity

	var cd := Aerodynamics.get_cd(reynolds) * params.drag_scale
	var cl := Aerodynamics.get_cl(reynolds, spin_ratio) * params.lift_scale

	# Drag force (opposite to velocity)
	var drag := -0.5 * cd * params.air_density * CROSS_SECTION * velocity * speed

	# Magnus force (perpendicular to velocity and spin axis)
	var magnus := Vector3.ZERO
	var omega_len := omega.length()
	if omega_len > 0.1:
		var omega_cross_vel := omega.cross(velocity)
		magnus = 0.5 * cl * params.air_density * CROSS_SECTION * omega_cross_vel * speed / omega_len

	return drag + magnus


## Calculate total torques acting on the ball
static func calculate_torques(
	velocity: Vector3,
	omega: Vector3,
	on_ground: bool,
	params: PhysicsParams
) -> Vector3:
	if on_ground:
		return calculate_ground_torques(velocity, omega, params)
	else:
		# Spin decay torque (exponential decay model)
		return -MOMENT_OF_INERTIA * omega / SPIN_DECAY_TAU


## Calculate ground friction torques
static func calculate_ground_torques(
	velocity: Vector3,
	omega: Vector3,
	params: PhysicsParams
) -> Vector3:
	var friction_torque := Vector3.ZERO
	var grass_torque := -6.0 * PI * params.grass_viscosity * RADIUS * omega

	# Calculate friction for torque
	var contact_velocity := velocity + omega.cross(-params.floor_normal * RADIUS)
	var tangent_velocity := contact_velocity - params.floor_normal * contact_velocity.dot(params.floor_normal)

	var friction_force := Vector3.ZERO
	if tangent_velocity.length() < 0.05:
		var flat_velocity := velocity - params.floor_normal * velocity.dot(params.floor_normal)
		var friction_dir := flat_velocity.normalized() if flat_velocity.length() > 0.01 else Vector3.ZERO
		friction_force = friction_dir * (-params.rolling_friction * MASS * 9.81)
	else:
		var slip_dir := tangent_velocity.normalized()
		friction_force = slip_dir * (-params.kinetic_friction * MASS * 9.81)

	if friction_force.length() > 0.001:
		friction_torque = (-params.floor_normal * RADIUS).cross(friction_force)

	return friction_torque + grass_torque


## Bounce calculation result
class BounceResult:
	var new_velocity: Vector3
	var new_omega: Vector3
	var new_state: PhysicsEnums.BallState

	func _init(vel: Vector3, omg: Vector3, st: PhysicsEnums.BallState) -> void:
		new_velocity = vel
		new_omega = omg
		new_state = st


## Calculate bounce physics when ball impacts surface
static func calculate_bounce(
	vel: Vector3,
	omega: Vector3,
	normal: Vector3,
	current_state: PhysicsEnums.BallState,
	params: PhysicsParams
) -> BounceResult:
	var new_state := PhysicsEnums.BallState.ROLLOUT if current_state == PhysicsEnums.BallState.FLIGHT else current_state

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

	if new_state == PhysicsEnums.BallState.ROLLOUT:
		print("  Bounce: spin=%.0f rpm, factor=%.3f, retention=%.3f" % [
			current_spin_rpm, spin_factor, tangential_retention
		])

	# Calculate new tangential speed
	var new_tangent_speed := tangential_retention * vel.length() * sin(impact_angle - params.critical_angle) - \
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
	var cor := get_coefficient_of_restitution(speed_normal)
	vel_normal = vel_normal * -cor

	var new_omega := omega_normal + omega_tangent
	var new_velocity := vel_normal + vel_tangent

	return BounceResult.new(new_velocity, new_omega, new_state)


## Get coefficient of restitution based on impact speed
static func get_coefficient_of_restitution(speed_normal: float) -> float:
	if speed_normal > 20.0:
		return 0.25  # High speed impacts
	elif speed_normal < 2.0:
		return 0.0  # Kill very small bounces
	else:
		# Typical COR curve for golf ball on turf
		return 0.45 - 0.0100 * speed_normal + 0.0002 * speed_normal * speed_normal
