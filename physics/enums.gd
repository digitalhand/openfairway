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
	FAIRWAY,       ## Normal fairway - good conditions with 35-60 yd rollout
	FAIRWAY_SOFT,  ## Soft/wet fairway - reduced rollout (~20-30 yds)
	ROUGH,         ## Longer grass, more friction
	FIRM           ## Hard ground, less friction
}
