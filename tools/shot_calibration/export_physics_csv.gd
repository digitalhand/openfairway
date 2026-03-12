extends SceneTree

const PhysicsExportDataScript = preload("res://tools/shot_calibration/physics_export_data.gd")

## Headless CSV exporter for all shot files using PhysicsAdapter simulation.
## Auto-discovers all *.json files in res://assets/data/ (skips non-shot files).
## Usage: godot --headless --script tools/shot_calibration/export_physics_csv.gd
## Optional: godot --headless --script tools/shot_calibration/export_physics_csv.gd -- --output=res://assets/data/calibration/physics.csv

func _init() -> void:
	var exporter = PhysicsExportDataScript.new()
	var rows := exporter.collect_rows()
	var output_path := exporter.resolve_output_path(PhysicsExportDataScript.DEFAULT_CSV_OUTPUT_PATH)
	var error := exporter.write_csv(rows, output_path)
	if error != OK:
		quit(1)
		return

	printerr("Wrote physics CSV to %s" % output_path)
	quit()
