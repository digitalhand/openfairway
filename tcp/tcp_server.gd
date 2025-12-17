extends Node

## TCP server for receiving shot data.
## Handles a single client at a time and emits hit_ball when payload is valid.

signal hit_ball(data: Dictionary)

const PORT := 49152

var tcp_server: TCPServer = TCPServer.new()
var tcp_connection: StreamPeerTCP = null
var tcp_connected: bool = false
var shot_data: Dictionary = {}

var resp_200 := {"Code": 200}

func _ready() -> void:
	tcp_server.listen(PORT)

func _process(_delta: float) -> void:
	if not tcp_connected:
		_accept_connection()
	else:
		_poll_connection()

func _accept_connection() -> void:
	tcp_connection = tcp_server.take_connection()
	if tcp_connection:
		tcp_connected = true
		print("TCP connected: ", tcp_connection.get_connected_host())

func _poll_connection() -> void:
	tcp_connection.poll()
	var tcp_status: StreamPeerTCP.Status = tcp_connection.get_status()
	match tcp_status:
		StreamPeerTCP.STATUS_NONE:
			_handle_disconnect()
		StreamPeerTCP.STATUS_CONNECTED:
			_read_payload()

func _handle_disconnect() -> void:
	tcp_connected = false
	tcp_connection = null
	shot_data.clear()
	print("TCP disconnected")

func _read_payload() -> void:
	var bytes_avail := tcp_connection.get_available_bytes()
	if bytes_avail <= 0:
		return

	var result := tcp_connection.get_data(bytes_avail)
	if result.size() < 2 or result[0] != OK:
		respond_error(501, "Socket read error")
		return

	var buf: PackedByteArray = result[1]
	var json_text := buf.get_string_from_utf8()
	_parse_and_emit(json_text)

func _parse_and_emit(json_text: String) -> void:
	var json := JSON.new()
	var error := json.parse(json_text)
	if error != OK:
		respond_error(501, "Bad JSON data")
		return

	if not (json.data is Dictionary):
		respond_error(501, "Invalid payload")
		return

	shot_data = json.data
	print("Launch monitor payload: ", json_text)

	var options: Variant = shot_data.get("ShotDataOptions", null)
	if not (options is Dictionary):
		return

	var contains_ball: bool = options.get("ContainsBallData", false)
	if contains_ball and shot_data.has("BallData") and shot_data["BallData"] is Dictionary:
		emit_signal("hit_ball", shot_data["BallData"])

func respond_error(code: int, message: String) -> void:
	if tcp_connection == null:
		return
	tcp_connection.poll()
	var tcp_status: StreamPeerTCP.Status = tcp_connection.get_status()
	if tcp_status == StreamPeerTCP.STATUS_NONE:
		_handle_disconnect()
		return
	if tcp_status == StreamPeerTCP.STATUS_CONNECTED:
		var resp := {"Code": code, "Message": message}
		tcp_connection.put_data(JSON.stringify(resp).to_ascii_buffer())

func _on_golf_ball_good_data() -> void:
	if tcp_connection == null:
		return
	tcp_connection.poll()
	var tcp_status: StreamPeerTCP.Status = tcp_connection.get_status()
	if tcp_status == StreamPeerTCP.STATUS_NONE:
		_handle_disconnect()
	elif tcp_status == StreamPeerTCP.STATUS_CONNECTED:
		tcp_connection.put_data(JSON.stringify(resp_200).to_ascii_buffer())

func _on_player_bad_data() -> void:
	respond_error(501, "Bad Ball Data")
