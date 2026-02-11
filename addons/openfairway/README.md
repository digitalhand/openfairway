# OpenFairway Physics

Realistic golf ball physics engine with aerodynamics, bounce, and surface interactions for Godot 4.5+ C# projects. Usable from both C# and GDScript.

## Table of Contents
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick Start (GDScript)](#quick-start-gdscript)
- [API Reference — GDScript Usage](#api-reference--gdscript-usage)
  - [BallPhysics](#ballphysics)
  - [PhysicsParams](#physicsparams)
  - [BounceResult](#bounceresult)
  - [Aerodynamics](#aerodynamics)
  - [Surface](#surface)
  - [PhysicsEnums](#physicsenums)
  - [PhysicsAdapter](#physicsadapter)
- [Full Example — GDScript Physics Loop](#full-example--gdscript-physics-loop)
- [Full Example — Headless Shot Simulation](#full-example--headless-shot-simulation-gdscript)
- [Units Convention](#units-convention)
- [Detailed Physics Documentation](#detailed-physics-documentation)
- [License](#license)

## Requirements

- **Godot 4.5+** with **.NET support** (the standard GDScript-only editor build will not work)
- The .NET build is required because this addon is written in C#
- GDScript projects **can** use this addon — Godot's cross-language scripting handles the interop automatically

## Installation

1. Copy the `addons/openfairway/` folder into your project's `addons/` directory
2. Build the C# project: **Build > Build Project** in the editor, or run `dotnet build` from the command line
3. Enable the plugin: **Project > Project Settings > Plugins > OpenFairway Physics**

## Quick Start (GDScript)

```gdscript
# Calculate forces on a ball in flight
var physics = BallPhysics.new()
var aero = Aerodynamics.new()

var params = PhysicsParams.new()
params.air_density = aero.get_air_density(0.0, 75.0, PhysicsEnums.Units.IMPERIAL)
params.air_viscosity = aero.get_dynamic_viscosity(75.0, PhysicsEnums.Units.IMPERIAL)
params.drag_scale = 1.0
params.lift_scale = 1.0
params.floor_normal = Vector3.UP

var velocity = Vector3(40.0, 15.0, 0.0)  # m/s
var omega = Vector3(0.0, 0.0, 300.0)     # rad/s (~2865 RPM)

var force = physics.calculate_forces(velocity, omega, false, params)
print("Force: ", force)
```

## API Reference — GDScript Usage

Godot automatically converts C# PascalCase to GDScript snake_case. All classes below extend `RefCounted` (or `Resource`) and are created with `.new()`.

### BallPhysics

Core force, torque, and bounce calculations.

```gdscript
var physics = BallPhysics.new()
```

**Constants** (read-only exported properties):

| GDScript property          | Value                | Description                  |
|----------------------------|----------------------|------------------------------|
| `physics.ball_mass`        | 0.04593 kg           | Regulation golf ball mass    |
| `physics.ball_radius`      | 0.02134 m            | Regulation golf ball radius  |
| `physics.ball_cross_section` | pi * r^2 m^2       | Cross-sectional area         |
| `physics.ball_moment_of_inertia` | 0.4 * m * r^2 | Moment of inertia            |
| `physics.spin_decay_tau`   | 5.0 s                | Spin decay time constant     |

**Methods:**

```gdscript
# Total forces acting on the ball (gravity + aero or ground friction)
# on_ground: true if ball is rolling, false if in flight
var force: Vector3 = physics.calculate_forces(velocity, omega, on_ground, params)

# Total torques acting on the ball (spin decay in air, friction torque on ground)
var torque: Vector3 = physics.calculate_torques(velocity, omega, on_ground, params)

# Bounce calculation when ball impacts a surface
# Returns a BounceResult with new_velocity, new_omega, new_state
var bounce: BounceResult = physics.calculate_bounce(vel, omega, normal, state, params)

# Coefficient of restitution for a given normal impact speed
var cor: float = physics.get_coefficient_of_restitution(speed_normal)
```

### PhysicsParams

Physics parameters passed to force/torque/bounce calculations. Extends `Resource`.

```gdscript
var params = PhysicsParams.new()
params.air_density = 1.225          # kg/m^3 (sea level, 15 C)
params.air_viscosity = 1.81e-05     # kg/(m*s)
params.drag_scale = 1.0             # Multiplier for drag coefficient tuning
params.lift_scale = 1.0             # Multiplier for lift coefficient tuning
params.kinetic_friction = 0.30      # Sliding friction coefficient
params.rolling_friction = 0.030     # Rolling resistance coefficient
params.grass_viscosity = 0.001      # Grass drag viscosity
params.critical_angle = 0.25        # Bounce critical angle (radians)
params.floor_normal = Vector3.UP    # Ground surface normal
params.rollout_impact_spin = 0.0    # Spin (RPM) when ball first landed
```

### BounceResult

Returned by `calculate_bounce()`. Extends `RefCounted`.

```gdscript
var result: BounceResult = physics.calculate_bounce(vel, omega, normal, state, params)
var new_vel: Vector3 = result.new_velocity
var new_omega: Vector3 = result.new_omega
var new_state: PhysicsEnums.BallState = result.new_state
```

### Aerodynamics

Air density, viscosity, and drag/lift coefficient calculations.

```gdscript
var aero = Aerodynamics.new()

# Air density from altitude and temperature (barometric formula)
# altitude: feet (Imperial) or meters (Metric)
# temp: Fahrenheit (Imperial) or Celsius (Metric)
var density: float = aero.get_air_density(altitude, temp, PhysicsEnums.Units.IMPERIAL)

# Dynamic air viscosity (Sutherland's formula)
var viscosity: float = aero.get_dynamic_viscosity(temp, PhysicsEnums.Units.IMPERIAL)

# Drag coefficient from Reynolds number
var cd: float = aero.get_cd(reynolds_number)

# Lift coefficient from Reynolds number and spin ratio (omega * radius / speed)
var cl: float = aero.get_cl(reynolds_number, spin_ratio)

# Maximum lift coefficient cap (read-only exported property)
print(aero.cl_max)  # 0.55
```

### Surface

Surface parameter presets for different ground types.

```gdscript
var surface = Surface.new()
var p: Dictionary = surface.get_params(PhysicsEnums.SurfaceType.FAIRWAY)

# Returned dictionary keys:
# "u_k"     - Kinetic friction coefficient (sliding)
# "u_kr"    - Rolling friction coefficient
# "nu_g"    - Grass drag viscosity
# "theta_c" - Critical bounce angle in radians
```

Available surface types:

| GDScript enum                           | Description                          |
|-----------------------------------------|--------------------------------------|
| `PhysicsEnums.SurfaceType.FAIRWAY`      | Firm fairway, good conditions        |
| `PhysicsEnums.SurfaceType.FAIRWAY_SOFT` | Soft/wet fairway, reduced rollout    |
| `PhysicsEnums.SurfaceType.ROUGH`        | Longer grass, more friction          |
| `PhysicsEnums.SurfaceType.FIRM`         | Hard ground, less friction           |

### PhysicsEnums

Container class exposing enums to GDScript. C# enum values are accessed in SCREAMING_SNAKE_CASE.

```gdscript
# Ball states
PhysicsEnums.BallState.REST      # Ball is stationary
PhysicsEnums.BallState.FLIGHT    # Ball is airborne
PhysicsEnums.BallState.ROLLOUT   # Ball is rolling after landing

# Unit systems
PhysicsEnums.Units.METRIC        # Meters, Celsius
PhysicsEnums.Units.IMPERIAL      # Feet/yards, Fahrenheit

# Surface types
PhysicsEnums.SurfaceType.FAIRWAY
PhysicsEnums.SurfaceType.FAIRWAY_SOFT
PhysicsEnums.SurfaceType.ROUGH
PhysicsEnums.SurfaceType.FIRM
```

### PhysicsAdapter

Headless shot simulator — runs a full shot from launch data and returns carry/total distances.

```gdscript
var adapter = PhysicsAdapter.new()

# simulate_shot_from_json takes a Dictionary with a "BallData" sub-dictionary
# Returns { "carry_yd": float, "total_yd": float }
var result: Dictionary = adapter.simulate_shot_from_json(shot_dict)
```

## Full Example — GDScript Physics Loop

A complete per-frame physics integration equivalent to what the game's `GolfBall` node does internally. Attach this to a `CharacterBody3D` with a collision shape.

```gdscript
extends CharacterBody3D

var physics := BallPhysics.new()
var aero := Aerodynamics.new()
var surface_helper := Surface.new()

var omega := Vector3.ZERO          # Angular velocity (rad/s)
var state: PhysicsEnums.BallState = PhysicsEnums.BallState.REST
var on_ground := false
var floor_normal := Vector3.UP
var rollout_impact_spin_rpm := 0.0

func _ready():
    pass

func hit_ball(speed_mph: float, vla_deg: float, hla_deg: float,
              total_spin_rpm: float, spin_axis_deg: float):
    var speed_mps := speed_mph * 0.44704

    velocity = Vector3(speed_mps, 0, 0) \
        .rotated(Vector3.FORWARD, deg_to_rad(-vla_deg)) \
        .rotated(Vector3.UP, deg_to_rad(-hla_deg))

    omega = Vector3(0.0, 0.0, total_spin_rpm * 0.10472) \
        .rotated(Vector3.RIGHT, deg_to_rad(spin_axis_deg))

    state = PhysicsEnums.BallState.FLIGHT
    on_ground = false
    rollout_impact_spin_rpm = 0.0

func _physics_process(delta: float):
    if state == PhysicsEnums.BallState.REST:
        return

    # Build physics params
    var params := PhysicsParams.new()
    params.air_density = aero.get_air_density(0.0, 75.0, PhysicsEnums.Units.IMPERIAL)
    params.air_viscosity = aero.get_dynamic_viscosity(75.0, PhysicsEnums.Units.IMPERIAL)
    params.drag_scale = 1.0
    params.lift_scale = 1.0
    params.floor_normal = floor_normal
    params.rollout_impact_spin = rollout_impact_spin_rpm

    # Load surface params
    var sp := surface_helper.get_params(PhysicsEnums.SurfaceType.FAIRWAY)
    params.kinetic_friction = sp["u_k"]
    params.rolling_friction = sp["u_kr"]
    params.grass_viscosity = sp["nu_g"]
    params.critical_angle = sp["theta_c"]

    # Calculate forces and torques
    var force := physics.calculate_forces(velocity, omega, on_ground, params)
    var torque := physics.calculate_torques(velocity, omega, on_ground, params)

    # Integrate
    velocity += (force / physics.ball_mass) * delta
    omega += (torque / physics.ball_moment_of_inertia) * delta

    # Move and handle collision
    var collision := move_and_collide(velocity * delta)
    if collision:
        var normal := collision.get_normal()
        if normal.y > 0.7:  # Ground hit
            if state == PhysicsEnums.BallState.FLIGHT:
                rollout_impact_spin_rpm = omega.length() / 0.10472
            var bounce := physics.calculate_bounce(velocity, omega, normal, state, params)
            velocity = bounce.new_velocity
            omega = bounce.new_omega
            state = bounce.new_state
            on_ground = true
            floor_normal = normal

    # Check for rest
    if velocity.length() < 0.1 and state != PhysicsEnums.BallState.REST:
        state = PhysicsEnums.BallState.REST
        velocity = Vector3.ZERO
        omega = Vector3.ZERO
```

## Full Example — Headless Shot Simulation (GDScript)

Use `PhysicsAdapter` to simulate a complete shot without any scene tree or physics nodes:

```gdscript
var adapter = PhysicsAdapter.new()

var shot = {
    "BallData": {
        "Speed": 150.0,       # mph
        "VLA": 12.5,          # vertical launch angle (degrees)
        "HLA": -2.0,          # horizontal launch angle (degrees)
        "TotalSpin": 2800.0,  # RPM
        "SpinAxis": 5.0       # degrees (0 = pure backspin)
    }
}

var result = adapter.simulate_shot_from_json(shot)
print("Carry: %.1f yds" % result["carry_yd"])
print("Total: %.1f yds" % result["total_yd"])
```

The adapter uses default conditions (75 F, sea level, fairway surface) and runs the full physics loop at 240 Hz internally.

## Units Convention

| Context            | Units                                      |
|--------------------|--------------------------------------------|
| Physics engine     | SI: meters, m/s, rad/s                     |
| JSON/TCP input     | Imperial: mph (speed), degrees (angles), RPM (spin) |
| Display conversion | Consumer's responsibility                  |

Conversion constants:
- **Speed**: 1 mph = 0.44704 m/s
- **Spin**: 1 RPM = 0.10472 rad/s
- **Distance**: 1 meter = 1.09361 yards

## Detailed Physics Documentation

See [`physics/README.md`](physics/README.md) for detailed force/torque formulas, the bounce model, aerodynamic coefficient curves, and tuning guidance.

## License

MIT — see [LICENSE](LICENSE).
