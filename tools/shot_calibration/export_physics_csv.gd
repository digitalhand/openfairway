extends SceneTree

## Headless CSV exporter for all shot files using PhysicsAdapter simulation.
## Auto-discovers all *.json files in res://assets/data/ (skips non-shot files).
## Usage: godot --headless --script tools/shot_calibration/export_physics_csv.gd > assets/data/calibration/physics.csv

const SKIP_FILES := []

func _init() -> void:
	var adapter := PhysicsAdapter.new()
	var dir := DirAccess.open("res://assets/data")
	if dir == null:
		printerr("ERROR: cannot open res://assets/data")
		quit(1)
		return

	var files: Array[String] = []
	dir.list_dir_begin()
	var fname := dir.get_next()
	while fname != "":
		if fname.ends_with(".json") and fname not in SKIP_FILES:
			files.append(fname)
		fname = dir.get_next()
	dir.list_dir_end()
	files.sort()

	# CSV header
	print("shot_name,filename,speed_mph,vla_deg,hla_deg,total_spin_rpm,spin_axis_deg,backspin_rpm,sidespin_rpm,carry_yd,total_yd,rollout_yd,apex_ft,hang_time_s,landing_speed_mps,landing_angle_deg,initial_re,initial_spin_ratio,initial_cd,initial_cl,peak_cl,carry_only_yd")

	for fname_iter in files:
		var path := "res://assets/data/" + fname_iter
		var file := FileAccess.open(path, FileAccess.READ)
		if file == null:
			printerr("WARN: cannot open %s" % path)
			continue

		var json := JSON.new()
		if json.parse(file.get_as_text()) != OK:
			printerr("WARN: bad JSON in %s" % path)
			continue

		var data: Dictionary = json.data

		# Skip files that don't look like shot data
		if not data.has("BallData") and not data.has("Speed"):
			printerr("WARN: skipping non-shot file %s" % fname_iter)
			continue

		var ball_data: Dictionary = data.get("BallData", data)
		var speed: float  = ball_data.get("Speed", 0.0)
		var vla: float    = ball_data.get("VLA", 0.0)
		var hla: float    = ball_data.get("HLA", 0.0)
		var total_spin: float = ball_data.get("TotalSpin", 0.0)
		var spin_axis: float  = ball_data.get("SpinAxis", 0.0)
		var backspin: float   = ball_data.get("BackSpin", 0.0)
		var sidespin: float   = ball_data.get("SideSpin", 0.0)

		# Shot name from filename without extension
		var shot_name: String = fname_iter.get_basename()

		# Full simulation (carry + rollout + total)
		var result: Dictionary = adapter.SimulateShotFromJson(data)
		var carry: float   = result.get("carry_yd", 0.0)
		var total: float   = result.get("total_yd", 0.0)
		var rollout: float = total - carry
		var apex: float    = result.get("apex_ft", 0.0)
		var hang: float    = result.get("hang_time_s", 0.0)
		var land_spd: float = result.get("landing_speed_mps", 0.0)
		var land_ang: float = result.get("landing_angle_deg", 0.0)
		var init_re: float  = result.get("initial_re", 0.0)
		var init_sr: float  = result.get("initial_spin_ratio", 0.0)
		var init_cd: float  = result.get("initial_cd", 0.0)
		var init_cl: float  = result.get("initial_cl", 0.0)
		var peak_cl: float  = result.get("peak_cl", 0.0)

		# Carry-only simulation
		var carry_result: Dictionary = adapter.SimulateCarryOnly(data)
		var carry_only: float = carry_result.get("carry_yd", 0.0)

		print("%s,%s,%.2f,%.2f,%.2f,%.1f,%.2f,%.1f,%.1f,%.1f,%.1f,%.1f,%.1f,%.2f,%.2f,%.2f,%.1f,%.6f,%.6f,%.6f,%.6f,%.1f" % [
			shot_name, fname_iter,
			speed, vla, hla, total_spin, spin_axis, backspin, sidespin,
			carry, total, rollout, apex, hang, land_spd, land_ang,
			init_re, init_sr, init_cd, init_cl, peak_cl, carry_only
		])

	quit()
