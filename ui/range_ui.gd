extends MarginContainer

signal hit_shot(data)

var selected_shot_path := "res://assets/data/drive_test_shot.json"
var shot_payloads := {
	"Drive": "res://assets/data/drive_test_shot.json",
	"Wood Low": "res://assets/data/wood_low_test_shot.json",
	"Wedge": "res://assets/data/wedge_test_shot.json",
	"Bump": "res://assets/data/bump_test_shot.json",
}

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	GlobalSettings.range_settings.shot_injector_enabled.setting_changed.connect(toggle_shot_injector)
	_populate_shot_types()


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(_delta: float) -> void:
	pass


func set_data(data: Dictionary) -> void:
	if GlobalSettings.range_settings.range_units.value == PhysicsEnums.Units.IMPERIAL:
		$GridCanvas/Distance.set_data(data["Distance"])
		$GridCanvas/Carry.set_data(data["Carry"])
		$GridCanvas/Side.set_data(data["Offline"])
		$GridCanvas/Apex.set_data(data["Apex"])
		$GridCanvas/Speed.set_units("mph")
		$GridCanvas/Speed.set_data(str(data["Speed"]))
		$GridCanvas/BackSpin.set_units("rpm")
		$GridCanvas/BackSpin.set_data(str(data["BackSpin"]))
		$GridCanvas/SideSpin.set_units("rpm")
		$GridCanvas/SideSpin.set_data(str(data["SideSpin"]))
		$GridCanvas/TotalSpin.set_units("rpm")
		$GridCanvas/TotalSpin.set_data(str(data["TotalSpin"]))
		$GridCanvas/SpinAxis.set_units("deg")
		$GridCanvas/SpinAxis.set_data(str(data["SpinAxis"]))
		$GridCanvas/VLA.set_data("%3.1f" % data["VLA"])
		$GridCanvas/HLA.set_data("%3.1f" % data["HLA"])
	else:
		$GridCanvas/Distance.set_data(data["Distance"])
		$GridCanvas/Carry.set_data(data["Carry"])
		$GridCanvas/Side.set_data(data["Offline"])
		$GridCanvas/Apex.set_data(data["Apex"])
		$GridCanvas/Speed.set_units("m/s")
		$GridCanvas/Speed.set_data(str(data["Speed"]))
		$GridCanvas/BackSpin.set_units("rpm")
		$GridCanvas/BackSpin.set_data(str(data["BackSpin"]))
		$GridCanvas/SideSpin.set_units("rpm")
		$GridCanvas/SideSpin.set_data(str(data["SideSpin"]))
		$GridCanvas/TotalSpin.set_units("rpm")
		$GridCanvas/TotalSpin.set_data(str(data["TotalSpin"]))
		$GridCanvas/SpinAxis.set_units("deg")
		$GridCanvas/SpinAxis.set_data(str(data["SpinAxis"]))
		$GridCanvas/VLA.set_data("%3.1f" % data["VLA"])
		$GridCanvas/HLA.set_data("%3.1f" % data["HLA"])


func _on_shot_injector_inject(data: Variant) -> void:
	emit_signal("hit_shot", data)


func toggle_shot_injector(value) -> void:
	$ShotInjector.visible = value


func set_total_distance(text: String) -> void:
		$OverlayLayer/TotalDistanceOverlay.text = text
		$OverlayLayer/TotalDistanceOverlay.visible = true


func clear_total_distance() -> void:
		$OverlayLayer/TotalDistanceOverlay.visible = false
		$OverlayLayer/TotalDistanceOverlay.text = "Total Distance --"


func _populate_shot_types() -> void:
	var option_button: OptionButton = $HBoxContainer/ShotTypeOption
	option_button.clear()
	var idx := 0
	for label in shot_payloads.keys():
		var path: String = shot_payloads[label]
		option_button.add_item(label)
		option_button.set_item_metadata(idx, path)
		idx += 1
	option_button.select(0)


func _on_shot_type_selected(index: int) -> void:
	var option_button: OptionButton = $HBoxContainer/ShotTypeOption
	var metadata: Variant = option_button.get_item_metadata(index)
	if typeof(metadata) == TYPE_STRING:
		selected_shot_path = metadata as String


func _on_hit_shot_pressed() -> void:
	var data: Dictionary = {}
	var file := FileAccess.open(selected_shot_path, FileAccess.READ)
	if file:
		var json_text := file.get_as_text()
		var json := JSON.new()
		if json.parse(json_text) == OK:
			var parsed: Variant = json.data
			if parsed is Dictionary and parsed.has("BallData"):
				data = (parsed["BallData"] as Dictionary).duplicate()

	if data.is_empty():
		print("Hit Shot: Failed to load shot data from ", selected_shot_path)
		return

	print("Hit Shot: Loaded from ", selected_shot_path)
	emit_signal("hit_shot", data)
