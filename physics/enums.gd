class_name PhysicsEnums
extends Resource

## Physics-related enumerations for the golf simulation.

## Ball flight states
enum BallState {
	REST,    ## Ball is stationary
	FLIGHT,  ## Ball is in the air
	ROLLOUT  ## Ball is rolling on ground after landing
}

## Measurement unit systems
enum Units {
	METRIC,   ## Meters, Celsius, etc.
	IMPERIAL  ## Yards, Fahrenheit, etc.
}

## Ground surface types affecting ball behavior
enum Surface {
	FAIRWAY,  ## Standard fairway conditions
	ROUGH,    ## Longer grass, more friction
	FIRM      ## Hard ground, less friction
}
