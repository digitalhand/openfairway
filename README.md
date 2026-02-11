# OpenFairway

OpenFairway is an open source golf game built with Godot 4.5 (.NET/C#).

## Table of Contents
- [Overview](#overview)
- [Features](#features)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Physics Add-on](#openfairway-physics-add-on)

## Overview
OpenFairway focuses on realistic ball flight and rollout simulation with a range-style play loop. The core physics live in the `addons/openfairway/` add-on and are shared by the in-game ball and the headless simulator. 
This game will focus on stylized visuals versus photorealistic visuals (e.g. GSPro). 

## Features
- Physics-based ball flight, bounce, and rollout
- Aerodynamics with drag and lift coefficient models
- Surface tuning for fairway, rough, soft, and firm conditions
- Range scene with UI input and optional TCP launch monitor payloads
- Phantom Camera integration for follow and reset behavior

## Project Structure
- `addons/openfairway/` standalone physics engine add-on (ball physics, aerodynamics, surface models)
- `addons/phantom_camera/` third-party camera controller plugin
- `courses/` scenes and controllers for range/course content
- `game/` gameplay nodes like the golf ball and shot tracker
- `utils/` shared helpers, settings, and formatting

## Getting Started
1. Install Godot 4.5 with .NET support.
2. Open the project folder in Godot.
3. Run the main scene (already configured in `project.godot`).

## OpenFairway Physics Add-on

The physics engine is packaged as a standalone Godot add-on in `addons/openfairway/`. Other Godot 4.5+ C# projects can use it by copying the folder into their own project.

### Installation

1. Copy the `addons/openfairway/` folder into your project's `addons/` directory.
2. In the Godot Editor, go to **Project > Project Settings > Plugins** and enable **OpenFairway Physics**.
3. All physics classes are compiled automatically by Godot's .NET SDK — no additional project references needed.

### Classes

| Class | Base | Description |
|-------|------|-------------|
| `BallPhysics` | `RefCounted` | Core force, torque, and bounce calculations |
| `PhysicsParams` | `Resource` | Physics parameters (exported properties, serializable) |
| `BounceResult` | `RefCounted` | Bounce calculation result |
| `Aerodynamics` | `RefCounted` | Drag/lift coefficients, air density, and viscosity helpers |
| `Surface` | `RefCounted` | Surface parameter presets (fairway, rough, soft, firm) |
| `PhysicsAdapter` | `RefCounted` | Headless shot simulator that runs a full shot from JSON input |
| `PhysicsEnums` | `Resource` | Shared enums (`BallState`, `Units`, `SurfaceType`) |

### Usage

Create instances and call methods each physics frame:

```csharp
var bp = new BallPhysics();
var aero = new Aerodynamics();

var physicsParams = new PhysicsParams(
    airDensity: aero.GetAirDensity(altitudeFt, tempF, PhysicsEnums.Units.Imperial),
    airViscosity: aero.GetDynamicViscosity(tempF, PhysicsEnums.Units.Imperial),
    dragScale: 1.0f,
    liftScale: 1.0f,
    kineticFriction: 0.30f,
    rollingFriction: 0.030f,
    grassViscosity: 0.0010f,
    criticalAngle: 0.25f,
    floorNormal: Vector3.Up
);

Vector3 force = bp.CalculateForces(velocity, omega, onGround, physicsParams);
Vector3 torque = bp.CalculateTorques(velocity, omega, onGround, physicsParams);
```

### Headless Simulation

Run a full shot from JSON without any scene or node:

```csharp
var shotJson = new Godot.Collections.Dictionary
{
    ["BallData"] = new Godot.Collections.Dictionary
    {
        ["Speed"] = 150.0,       // mph
        ["VLA"] = 12.5,          // vertical launch angle (degrees)
        ["HLA"] = 0.0,           // horizontal launch angle (degrees)
        ["TotalSpin"] = 2800,    // RPM
        ["SpinAxis"] = 0.0       // degrees
    }
};
var adapter = new PhysicsAdapter();
var result = adapter.SimulateShotFromJson(shotJson);
```

### Full Documentation

See [addons/openfairway/physics/README.md](addons/openfairway/physics/README.md) for detailed force/torque formulas, bounce model, and tuning guide.
