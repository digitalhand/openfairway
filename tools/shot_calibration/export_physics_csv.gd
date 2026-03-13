extends SceneTree

const PhysicsExportDataScript = preload("res://tools/shot_calibration/physics_export_data.gd")

## Headless CSV exporter for all shot files using PhysicsAdapter simulation.
## Auto-discovers all *.json files in res://assets/data/ (skips non-shot files).
## Usage: godot --headless --script tools/shot_calibration/export_physics_csv.gd
## Optional: godot --headless --script tools/shot_calibration/export_physics_csv.gd -- --output=res://assets/data/calibration/physics.csv

func _init() -> void:
	var exporter = PhysicsExportDataScript.new()
	var dirs_spec := exporter.resolve_dirs_spec()
	var rows: Array[Dictionary]
	var default_output := PhysicsExportDataScript.DEFAULT_CSV_OUTPUT_PATH

	if dirs_spec != "":
		rows = exporter.collect_rows_multi(dirs_spec)
	else:
		var session_path := exporter.resolve_session_path()
		var data_dir_override := ""
		if session_path != "":
			data_dir_override = session_path
			default_output = session_path + "/physics.csv"
		rows = exporter.collect_rows(data_dir_override)

	var output_path := exporter.resolve_output_path(default_output)
	var error := exporter.write_csv(rows, output_path)
	if error != OK:
		quit(1)
		return

	printerr("Wrote physics CSV to %s" % output_path)
	quit()
