class_name PhysicsExportData
extends RefCounted

const DATA_DIR_PATH := "res://assets/data"
const SKIP_FILES := []
const CSV_HEADER := "shot_name,filename,speed_mph,vla_deg,hla_deg,total_spin_rpm,spin_axis_deg,backspin_rpm,sidespin_rpm,carry_yd,total_yd,rollout_yd,apex_ft,hang_time_s,landing_speed_mps,landing_angle_deg,initial_re,initial_spin_ratio,initial_cd,initial_cl,peak_cl,carry_only_yd"
const DEFAULT_CSV_OUTPUT_PATH := "res://assets/data/calibration/physics.csv"
const DEFAULT_JSON_OUTPUT_PATH := "res://assets/data/calibration/physics.json"

func collect_rows() -> Array[Dictionary]:
	var adapter := PhysicsAdapter.new()
	var dir := DirAccess.open(DATA_DIR_PATH)
	if dir == null:
		push_error("ERROR: cannot open %s" % DATA_DIR_PATH)
		return []

	var files: Array[String] = []
	dir.list_dir_begin()
	var fname := dir.get_next()
	while fname != "":
		if fname.ends_with(".json") and fname not in SKIP_FILES:
			files.append(fname)
		fname = dir.get_next()
	dir.list_dir_end()
	files.sort()

	var rows: Array[Dictionary] = []
	for fname_iter in files:
		var row := _collect_row(adapter, fname_iter)
		if not row.is_empty():
			rows.append(row)

	return rows

func to_keyed_dictionary(rows: Array[Dictionary]) -> Dictionary:
	var output := {}
	for row in rows:
		output[row["shot_name"]] = row
	return output

func resolve_output_path(default_output_path: String) -> String:
	var args := OS.get_cmdline_user_args()
	for i in range(args.size()):
		var arg: String = args[i]
		if arg.begins_with("--output="):
			return _normalize_output_path(arg.trim_prefix("--output="))
		if arg == "--output" and i + 1 < args.size():
			return _normalize_output_path(args[i + 1])
	return default_output_path

func write_csv(rows: Array[Dictionary], output_path: String) -> Error:
	var lines: Array[String] = [CSV_HEADER]
	for row in rows:
		lines.append(format_csv_row(row))
	return _write_text(output_path, "\n".join(lines) + "\n")

func write_json(rows: Array[Dictionary], output_path: String) -> Error:
	return _write_text(output_path, JSON.stringify(to_keyed_dictionary(rows), "\t") + "\n")

func format_csv_row(row: Dictionary) -> String:
	return "%s,%s,%.2f,%.2f,%.2f,%.1f,%.2f,%.1f,%.1f,%.1f,%.1f,%.1f,%.1f,%.2f,%.2f,%.2f,%.1f,%.6f,%.6f,%.6f,%.6f,%.1f" % [
		row["shot_name"], row["filename"],
		row["speed_mph"], row["vla_deg"], row["hla_deg"], row["total_spin_rpm"],
		row["spin_axis_deg"], row["backspin_rpm"], row["sidespin_rpm"],
		row["carry_yd"], row["total_yd"], row["rollout_yd"], row["apex_ft"],
		row["hang_time_s"], row["landing_speed_mps"], row["landing_angle_deg"],
		row["initial_re"], row["initial_spin_ratio"], row["initial_cd"],
		row["initial_cl"], row["peak_cl"], row["carry_only_yd"],
	]

func _collect_row(adapter: PhysicsAdapter, fname_iter: String) -> Dictionary:
	var path := "%s/%s" % [DATA_DIR_PATH, fname_iter]
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		push_warning("WARN: cannot open %s" % path)
		return {}

	var json := JSON.new()
	if json.parse(file.get_as_text()) != OK:
		push_warning("WARN: bad JSON in %s" % path)
		return {}

	var data: Dictionary = json.data
	if not data.has("BallData") and not data.has("Speed"):
		push_warning("WARN: skipping non-shot file %s" % fname_iter)
		return {}

	var ball_data: Dictionary = data.get("BallData", data)
	var speed: float = ball_data.get("Speed", 0.0)
	var vla: float = ball_data.get("VLA", 0.0)
	var hla: float = ball_data.get("HLA", 0.0)
	var total_spin: float = ball_data.get("TotalSpin", 0.0)
	var spin_axis: float = ball_data.get("SpinAxis", 0.0)
	var backspin: float = ball_data.get("BackSpin", 0.0)
	var sidespin: float = ball_data.get("SideSpin", 0.0)
	var shot_name := fname_iter.get_basename()

	var result: Dictionary = adapter.SimulateShotFromJson(data)
	var carry: float = result.get("carry_yd", 0.0)
	var total: float = result.get("total_yd", 0.0)
	var carry_result: Dictionary = adapter.SimulateCarryOnlyFromJson(data)

	return {
		"shot_name": shot_name,
		"filename": fname_iter,
		"speed_mph": speed,
		"vla_deg": vla,
		"hla_deg": hla,
		"total_spin_rpm": total_spin,
		"spin_axis_deg": spin_axis,
		"backspin_rpm": backspin,
		"sidespin_rpm": sidespin,
		"carry_yd": carry,
		"total_yd": total,
		"rollout_yd": total - carry,
		"apex_ft": result.get("apex_ft", 0.0),
		"hang_time_s": result.get("hang_time_s", 0.0),
		"landing_speed_mps": result.get("landing_speed_mps", 0.0),
		"landing_angle_deg": result.get("landing_angle_deg", 0.0),
		"initial_re": result.get("initial_re", 0.0),
		"initial_spin_ratio": result.get("initial_spin_ratio", 0.0),
		"initial_cd": result.get("initial_cd", 0.0),
		"initial_cl": result.get("initial_cl", 0.0),
		"peak_cl": result.get("peak_cl", 0.0),
		"carry_only_yd": carry_result.get("carry_yd", 0.0),
	}

func _normalize_output_path(path: String) -> String:
	if path.begins_with("res://") or path.begins_with("user://") or path.is_absolute_path():
		return path
	return ProjectSettings.globalize_path(path)

func _write_text(output_path: String, content: String) -> Error:
	var absolute_path := ProjectSettings.globalize_path(output_path)
	var dir_path := absolute_path.get_base_dir()
	var mkdir_error := DirAccess.make_dir_recursive_absolute(dir_path)
	if mkdir_error != OK:
		push_error("ERROR: cannot create output directory %s (%s)" % [dir_path, mkdir_error])
		return mkdir_error

	var file := FileAccess.open(output_path, FileAccess.WRITE)
	if file == null:
		var open_error := FileAccess.get_open_error()
		push_error("ERROR: cannot write %s (%s)" % [output_path, open_error])
		return open_error

	file.store_string(content)
	return OK
