extends SceneTree

## Headless benchmark runner for all test shots.
## Usage: godot --headless --script run_benchmarks.gd

var shots := {
	"Drive":      "res://assets/data/drive_test_shot.json",
	"Wood Low":   "res://assets/data/wood_low_test_shot.json",
	"Wedge":      "res://assets/data/wedge_test_shot.json",
	"Wedge2":      "res://assets/data/wedge_test_shot2.json",
	"Bump":       "res://assets/data/bump_test_shot.json",
	"Bump & Run": "res://assets/data/bump_and_run.json",
	"Approach":   "res://assets/data/approach_test_shot.json",
	"Mid Iron":   "res://assets/data/approach_mid_iron_test_shot.json",
	"Topped":     "res://assets/data/topped_test_shot.json",
	"Checked":    "res://assets/data/checked_test_shot.json",
	"Flop":       "res://assets/data/flop_test_shot.json",
	"Chip":       "res://assets/data/chip_test_shot.json",
	"Putt 10ft":  "res://assets/data/putt_ten_feet.json",
	"Free Shot:  "res://assets/data/free_shot.json",
}

func _init() -> void:
	var adapter := PhysicsAdapter.new()

	print("")
	print("=" .repeat(100))
	print("DISTANCE BENCHMARK REPORT")
	print("=" .repeat(100))
	print("%-12s | %8s | %8s | %8s | %8s | %10s | %8s | %8s" % [
		"Shot", "Speed", "VLA", "Spin", "Axis",
		"Carry (yd)", "Total (yd)", "Rollout"])
	print("-" .repeat(100))

	for shot_name in shots:
		var path: String = shots[shot_name]
		var file := FileAccess.open(path, FileAccess.READ)
		if file == null:
			print("%-12s | ERROR: cannot open %s" % [shot_name, path])
			continue

		var json := JSON.new()
		if json.parse(file.get_as_text()) != OK:
			print("%-12s | ERROR: bad JSON in %s" % [shot_name, path])
			continue

		var data: Dictionary = json.data
		var ball_data: Dictionary = data.get("BallData", data)

		var speed: float  = ball_data.get("Speed", 0.0)
		var vla: float    = ball_data.get("VLA", 0.0)
		var spin: float   = ball_data.get("TotalSpin", 0.0)
		var axis: float   = ball_data.get("SpinAxis", 0.0)

		var result := adapter.SimulateShotFromJson(data)
		var carry: float  = result.get("carry_yd", 0.0)
		var total: float  = result.get("total_yd", 0.0)
		var rollout: float = total - carry

		print("%-12s | %5.1f mph | %6.2f° | %6.0f rpm | %6.2f° | %8.1f yd | %8.1f yd | %6.1f yd" % [
			shot_name, speed, vla, spin, axis,
			carry, total, rollout])

	print("=" .repeat(100))
	print("")
	quit()
