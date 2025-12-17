class_name ShotFormatter
extends RefCounted

## Formats ball/shot data for UI display with unit conversion.

const METERS_TO_YARDS := 1.09361
const METERS_TO_FEET := 3.28084
const MPH_TO_MPS := 0.44704


## Format ball data for UI display.
## [br][br]
## Converts units and calculates derived spin values as needed.
## [br][br]
## [param raw_ball_data]: Raw shot data from launch monitor or injector
## [param shot_tracker]: Reference to ShotTracker for live measurements
## [param units]: Unit system to use for display
## [param show_distance]: Whether to update distance (false keeps previous value)
## [param prev_data]: Previous display data (for preserving Distance when not updating)
static func format_ball_display(
	raw_ball_data: Dictionary,
	shot_tracker: Node,
	units: PhysicsEnums.Units,
	show_distance: bool,
	prev_data: Dictionary = {}
) -> Dictionary:
	var ball_data: Dictionary = {}

	# Parse spin data
	var spin := _parse_spin(raw_ball_data)

	if units == PhysicsEnums.Units.IMPERIAL:
		ball_data = _format_imperial(raw_ball_data, shot_tracker, show_distance, prev_data, spin)
	else:
		ball_data = _format_metric(raw_ball_data, shot_tracker, show_distance, prev_data, spin)

	# Common fields (same in both unit systems)
	ball_data["BackSpin"] = str(int(spin.back))
	ball_data["SideSpin"] = str(int(spin.side))
	ball_data["TotalSpin"] = str(int(spin.total))
	ball_data["SpinAxis"] = "%3.1f" % spin.axis
	ball_data["VLA"] = raw_ball_data.get("VLA", 0.0)
	ball_data["HLA"] = raw_ball_data.get("HLA", 0.0)

	return ball_data


static func _format_imperial(
	raw_data: Dictionary,
	tracker: Node,
	show_distance: bool,
	prev_data: Dictionary,
	_spin: Dictionary
) -> Dictionary:
	var data: Dictionary = {}

	# Distance
	if show_distance:
		data["Distance"] = "%.1f" % (tracker.get_distance() * METERS_TO_YARDS)
	else:
		data["Distance"] = prev_data.get("Distance", "---")

	# Carry
	var carry_val: float = tracker.carry
	if carry_val <= 0 and raw_data.has("CarryDistance"):
		carry_val = float(raw_data.get("CarryDistance", 0.0))
	data["Carry"] = "%.1f" % (carry_val * METERS_TO_YARDS)

	# Apex (convert meters to feet)
	data["Apex"] = "%.1f" % (tracker.apex * METERS_TO_FEET)

	# Side distance
	var side_distance: float = tracker.get_side_distance() * METERS_TO_YARDS
	data["Offline"] = _format_side_distance(side_distance)

	# Speed (already in mph)
	data["Speed"] = "%.1f" % raw_data.get("Speed", 0.0)

	return data


static func _format_metric(
	raw_data: Dictionary,
	tracker: Node,
	show_distance: bool,
	prev_data: Dictionary,
	_spin: Dictionary
) -> Dictionary:
	var data: Dictionary = {}

	# Distance
	if show_distance:
		data["Distance"] = "%.1f" % tracker.get_distance()
	else:
		data["Distance"] = prev_data.get("Distance", "---")

	# Carry
	var carry_val: float = tracker.carry
	if carry_val <= 0 and raw_data.has("CarryDistance"):
		carry_val = float(raw_data.get("CarryDistance", 0.0))
	data["Carry"] = "%.1f" % carry_val

	# Apex (meters)
	data["Apex"] = "%.1f" % tracker.apex

	# Side distance
	var side_distance: float = tracker.get_side_distance()
	data["Offline"] = _format_side_distance(side_distance)

	# Speed (convert mph to m/s)
	data["Speed"] = "%.1f" % (float(raw_data.get("Speed", 0.0)) * MPH_TO_MPS)

	return data


static func _format_side_distance(distance: float) -> String:
	var direction := "R" if distance >= 0 else "L"
	return direction + ("%.1f" % absf(distance))


static func _parse_spin(raw_data: Dictionary) -> Dictionary:
	var has_backspin := raw_data.has("BackSpin")
	var has_sidespin := raw_data.has("SideSpin")
	var has_total := raw_data.has("TotalSpin")
	var has_axis := raw_data.has("SpinAxis")

	var backspin: float = float(raw_data.get("BackSpin", 0.0))
	var sidespin: float = float(raw_data.get("SideSpin", 0.0))
	var total_spin: float = float(raw_data.get("TotalSpin", 0.0))
	var spin_axis: float = float(raw_data.get("SpinAxis", 0.0))

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

	return {
		"back": backspin,
		"side": sidespin,
		"total": total_spin,
		"axis": spin_axis
	}
