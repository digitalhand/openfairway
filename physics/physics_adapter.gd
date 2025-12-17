extends Node
class_name PhysicsAdapter

const MPS_PER_MPH := 0.44704
const YARDS_PER_METER := 1.09361
const START_HEIGHT := 0.02
const DEFAULT_TEMP_F := 75.0
const DEFAULT_ALT_FT := 0.0
const MAX_TIME := 12.0
const DT := 1.0 / 240.0


static func simulate_shot_from_json(shot: Dictionary) -> Dictionary:
	var ball_dict: Dictionary = shot.get("BallData", shot)
	if not (ball_dict is Dictionary) or ball_dict.is_empty():
		push_error("Shot JSON missing BallData")
		return {}

	var speed_mps: float = float(ball_dict.get("Speed", 0.0)) * MPS_PER_MPH
	var vla: float = float(ball_dict.get("VLA", 0.0))
	var hla: float = float(ball_dict.get("HLA", 0.0))
	var spin_data := _parse_spin(ball_dict)
	var total_spin: float = float(spin_data["total"])
	var spin_axis: float = float(spin_data["axis"])

	var velocity := Vector3(speed_mps, 0, 0) \
		.rotated(Vector3.FORWARD, deg_to_rad(-vla)) \
		.rotated(Vector3.UP, deg_to_rad(-hla))

	var omega := Vector3(0.0, 0.0, total_spin * 0.10472) \
		.rotated(Vector3.RIGHT, deg_to_rad(spin_axis))

	var flat_velocity := Vector3(velocity.x, 0.0, velocity.z)
	var shot_dir := flat_velocity.normalized() if flat_velocity.length() > 0.001 else Vector3.RIGHT

	var params := _create_params(Vector3.UP, PhysicsEnums.Surface.FAIRWAY)

	var pos := Vector3(0.0, START_HEIGHT, 0.0)
	var state := PhysicsEnums.BallState.FLIGHT
	var on_ground := false
	var carry_m := 0.0
	var carry_recorded := false

	var steps := int(MAX_TIME / DT)
	for _i in range(steps):
		var force := BallPhysics.calculate_forces(velocity, omega, on_ground, params)
		var torque := BallPhysics.calculate_torques(velocity, omega, on_ground, params)

		velocity += (force / BallPhysics.MASS) * DT
		omega += (torque / BallPhysics.MOMENT_OF_INERTIA) * DT

		pos += velocity * DT

		var has_impact := pos.y <= 0.0 and (velocity.y < -0.01 or state == PhysicsEnums.BallState.FLIGHT)
		if has_impact:
			pos.y = 0.0
			var bounce := BallPhysics.calculate_bounce(velocity, omega, Vector3.UP, state, params)
			velocity = bounce.new_velocity
			omega = bounce.new_omega
			state = bounce.new_state
			on_ground = state != PhysicsEnums.BallState.FLIGHT
			velocity.y = maxf(velocity.y, 0.0)

			if not carry_recorded:
				carry_m = maxf(pos.dot(shot_dir), 0.0)
				carry_recorded = true
		else:
			if pos.y < 0.0:
				pos.y = 0.0
				velocity.y = maxf(velocity.y, 0.0)
			on_ground = state != PhysicsEnums.BallState.FLIGHT and pos.y <= 0.02

		var speed := velocity.length()
		if on_ground and speed < 0.05 and omega.length() < 0.5:
			state = PhysicsEnums.BallState.REST
			velocity = Vector3.ZERO
			omega = Vector3.ZERO
			break

	var total_m := maxf(pos.dot(shot_dir), 0.0)
	if not carry_recorded:
		carry_m = total_m

	return {
		"carry_yd": carry_m * YARDS_PER_METER,
		"total_yd": total_m * YARDS_PER_METER
	}


static func _parse_spin(data: Dictionary) -> Dictionary:
	var has_backspin := data.has("BackSpin")
	var has_sidespin := data.has("SideSpin")
	var has_total := data.has("TotalSpin")
	var has_axis := data.has("SpinAxis")

	var backspin: float = float(data.get("BackSpin", 0.0))
	var sidespin: float = float(data.get("SideSpin", 0.0))
	var total_spin: float = float(data.get("TotalSpin", 0.0))
	var spin_axis: float = float(data.get("SpinAxis", 0.0))

	if total_spin == 0.0 and (has_backspin or has_sidespin):
		total_spin = sqrt(backspin * backspin + sidespin * sidespin)

	if not has_axis and (has_backspin or has_sidespin):
		spin_axis = rad_to_deg(atan2(sidespin, backspin))

	if has_total and has_axis:
		if not has_backspin:
			backspin = total_spin * cos(deg_to_rad(spin_axis))
		if not has_sidespin:
			sidespin = total_spin * sin(deg_to_rad(spin_axis))

	return {
		"backspin": backspin,
		"sidespin": sidespin,
		"total": total_spin,
		"axis": spin_axis
	}


static func _create_params(floor_normal: Vector3, surface: PhysicsEnums.Surface) -> BallPhysics.PhysicsParams:
	var surface_params := Surface.get_params(surface)
	var air_density := Aerodynamics.get_air_density(DEFAULT_ALT_FT, DEFAULT_TEMP_F, PhysicsEnums.Units.IMPERIAL)
	var air_viscosity := Aerodynamics.get_dynamic_viscosity(DEFAULT_TEMP_F, PhysicsEnums.Units.IMPERIAL)

	return BallPhysics.PhysicsParams.new(
		air_density,
		air_viscosity,
		1.0,
		1.0,
		surface_params["u_k"],
		surface_params["u_kr"],
		surface_params["nu_g"],
		surface_params["theta_c"],
		floor_normal
	)
