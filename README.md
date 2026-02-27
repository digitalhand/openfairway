# OpenFairway Physics

Realistic golf ball physics engine for Godot 4.5+ (.NET/C#). Provides force, torque, bounce, and surface interaction calculations usable from both C# and GDScript. YouTube Video [Demo of OpenFairway v1.0.2](https://youtu.be/IYSF5w6ROzo)

 <img src="assets/images/logo.png" width="50%">

[![CI/CD Pipeline](https://github.com/digitalhand/openfairway/workflows/CI%2FCD%20Pipeline/badge.svg)](https://github.com/digitalhand/openfairway/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Godot 4.5+](https://img.shields.io/badge/Godot-4.5+-478CBF)](https://godotengine.org/)

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Camera Updates (Hole Scene)](#camera-updates-hole-scene)
- [Marker Controls](#marker-controls)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Hole HUD Reusable Components](#hole-hud-reusable-components)
- [Surface Zones (Local Terrain Overrides)](#surface-zones-local-terrain-overrides)
- [Distance Benchmarks](#distance-benchmarks)
- [Known Feature Gaps](#known-feature-gaps)
- [Addon Classes](#addon-classes)
- [Addon Structure](#addon-structure)
- [Documentation](#documentation)
- [Units Convention](#units-convention)
- [License](#license)

## Features

- Aerodynamic drag and Magnus lift from wind-tunnel polynomial fits
- Bounce model with spin-dependent COR and tangential retention (Penner)
- Surface presets for fairway, rough, soft, and firm conditions
- Area-based surface zone overrides (`SurfaceZone`) for local terrain patches
- Spin-based ground friction with "check up" behavior for high-spin shots
- Launch monitor spin parsing (BackSpin/SideSpin or TotalSpin/SpinAxis)
- Headless shot simulation via `PhysicsAdapter` — no scene tree required

## Requirements

- **Godot 4.5+** with **.NET support**
- **.NET 8.0 SDK** (or later)

GDScript projects can use this addon — Godot's cross-language scripting handles the interop automatically, but the .NET editor build is required.

## Installation

1. Copy `addons/openfairway/` into your project's `addons/` directory.
2. **Ensure your project has a C# solution.** If you don't have a `.csproj`/`.sln` yet, generate them via **Project > Tools > C# > Create C# Solution** in the Godot editor. Alternatively, create any temporary C# script (Node > Attach Script > Language: C#) and Godot will generate both files automatically.
3. Build your project: **Build > Build Project** in the editor (`Alt+B`), or `dotnet build YourProject.csproj` from the command line.
4. Enable the plugin: **Project > Project Settings > Plugins > OpenFairway Physics**.

## Quick Start

### C#

```csharp
var bp = new BallPhysics();
var aero = new Aerodynamics();

var p = new PhysicsParams(
    airDensity: aero.GetAirDensity(0f, 75f, PhysicsEnums.Units.Imperial),
    airViscosity: aero.GetDynamicViscosity(75f, PhysicsEnums.Units.Imperial),
    dragScale: 1f, liftScale: 1f,
    kineticFriction: 0.30f, rollingFriction: 0.030f,
    grassViscosity: 0.001f, criticalAngle: 0.25f,
    floorNormal: Vector3.Up);

Vector3 force = bp.CalculateForces(velocity, omega, onGround, p);
Vector3 torque = bp.CalculateTorques(velocity, omega, onGround, p);
```

### GDScript

```gdscript
var physics = BallPhysics.new()
var aero = Aerodynamics.new()

var params = PhysicsParams.new()
params.air_density = aero.get_air_density(0.0, 75.0, PhysicsEnums.Units.Imperial)
params.air_viscosity = aero.get_dynamic_viscosity(75.0, PhysicsEnums.Units.Imperial)
params.drag_scale = 1.0
params.lift_scale = 1.0
params.floor_normal = Vector3.UP

var force = physics.calculate_forces(velocity, omega, false, params)
```

### Headless Simulation

Run a full shot with no scene tree:

```csharp
var adapter = new PhysicsAdapter();
var result = adapter.SimulateShotFromJson(new Godot.Collections.Dictionary
{
    ["BallData"] = new Godot.Collections.Dictionary
    {
        ["Speed"] = 150.0,       // mph
        ["VLA"] = 12.5,          // degrees
        ["HLA"] = 0.0,           // degrees
        ["TotalSpin"] = 2800,    // RPM
        ["SpinAxis"] = 0.0       // degrees
    }
});
// result["carry_yd"], result["total_yd"]
```

## Camera Updates (Hole Scene)

The primary gameplay scene (`res://courses/airways_fresno/hole_1/hole_1.tscn`) uses `ShotCameraController` for an orbit + follow workflow:

- At rest, use `ui_left` / `ui_right` (left/right arrows by default) to orbit the camera around the ball.
- A temporary ground aim marker appears while orbiting to show the current launch direction.
- Shot launch direction is derived from camera aim and applied as world yaw to the ball launch vector.
- On shot start, camera follow snaps immediately to the follow offset before enabling follow mode to avoid drift/swing.
- On ball rest, camera follow is frozen, then camera resets behind the current lie after the configured reset delay.

Keyboard controls are listed in [Keyboard Shortcuts](#keyboard-shortcuts).

## Marker Controls

Marker rendering is driven by `ShotMarkerController` and displayed by `ui/MarkerHUD.cs`.

- Left-click on terrain (not over UI) while the ball is at rest to place a player marker.
- A placed marker immediately reorients camera yaw to the selected point.
- Flag marker position comes from the active target reference (flag pole if present, otherwise fallback target nodes).
- Markers are suppressed during shot launch/flight and during goal countdown.
- Round reset clears player marker selection and repopulates the flag marker snapshot.

## Keyboard Shortcuts

- `F` toggles fullscreen/windowed mode.
- `P` toggles shot controls visibility.
- `H` (`hit`) injects a local test shot.
- `R` (`reset`) resets shot display/camera/ball state.
- `Tab` triggers the same round reset behavior as `R`.
- `ui_left` / `ui_right` (left/right arrows by default) orbit around the ball at rest.

## Hole HUD Reusable Components

Current hole-scene HUD logic is composed from reusable components:

- `utils/MeasurementUtils.cs`
  - Centralized distance/elevation conversions (`meters -> yards`, `meters -> feet`).
  - Used by both target HUD and world marker calculations.
- `ui/ElevationPresenter.cs`
  - Centralized elevation presentation (arrow direction, signed text, and color selection).
  - Keeps target elevation and marker elevation visually consistent.
- `ui/LayoutPersistenceService.cs`
  - Centralized panel layout save/load/apply behavior.
  - Persists panel position and visibility in `user://layout.cfg` for reusable layout management.

Hole scene integration points:

- `courses/airways_fresno/hole_1/Hole1.cs`
  - Orchestrates shot launch, target refresh, marker snapshots, and score/stroke updates.
- `game/camera/ShotCameraController.cs`
  - Handles orbit/follow behavior and click-to-aim camera recentering.
- `game/markers/ShotMarkerController.cs`
  - Builds flag/player marker snapshots and publishes updates.
- `ui/GameplayUI.cs`, `ui/CourseHud.cs`, `ui/MarkerHUD.cs`
  - Render shot data, elevation visuals, score card state, and world markers.

The reusable scoring label mapper for per-course par logic remains in `game/scoring/ScoreMapper.cs` and course header metadata is defined in `game/scoring/CourseCatalog.cs`.

### Component Diagram

```plantuml
@startuml
skinparam monochrome true

class GameplayUI
class CourseHud
class MarkerHUD
class ShotCameraController
class ShotMarkerController
class TargetReferenceResolver
class MeasurementUtils
class ElevationPresenter
class LayoutPersistenceService
class GridCanvas
class ScoreMapper
class CourseCatalog
class Hole1

Hole1 --> ShotCameraController : orbit + follow + launch yaw
Hole1 --> ShotMarkerController : marker state + snapshots
Hole1 --> TargetReferenceResolver : terrain and target queries
Hole1 --> GameplayUI : HUD + marker updates
Hole1 --> ScoreMapper : round-end label
Hole1 --> CourseCatalog : course card data
ShotCameraController --> ShotMarkerController : click/yaw marker selection
ShotMarkerController --> MeasurementUtils : distance/elevation math
GameplayUI --> CourseHud : shot + target + score rendering
GameplayUI --> MarkerHUD : apply marker snapshot
CourseHud --> ElevationPresenter : elevation text + color
GridCanvas --> LayoutPersistenceService : panel layout save/load

@enduml
```

## Surface Zones (Local Terrain Overrides)

Use `SurfaceZone` to apply local surface physics to patches like tall grass, cart paths, or special lies.

1. Add an `Area3D` where you want the surface override.
2. Add a `CollisionShape3D` under that `Area3D` to define the patch volume.
3. Attach `res://game/SurfaceZone.cs` to the `Area3D`.
4. In the Inspector, set `SurfaceType` (for tall grass use `Rough`).
5. Ensure collision layers/masks allow the ball (`ShotTracker/Ball`) to enter/exit the area.

Behavior details:

- Entering a zone applies that zone's `SurfaceType` immediately.
- Exiting restores the previous active zone surface (stacked overlap support).
- If no zone is active, the ball falls back to the active hole `SurfaceType` setting.

Current surface tuning intent:

- `Fairway` is slowed vs prior baseline to reduce excessive rollout.
- `Firm` is configured as a hard pavement/concrete style lie and matches the prior fast fairway baseline.
- `FairwaySoft` and `Rough` are progressively slower than `Fairway`.

## Distance Benchmarks

Run all 9 test shots headlessly and produce a carry/total/rollout distance table:

```bash
godot --headless --script run_benchmarks.gd
```

This is the standard way to validate physics changes. See `tests/PhysicsTests/README.md` for baseline distances and the full testing workflow.

## Known Feature Gaps

- Main menu has a visual `RangeTile`, but only `CoursesTile` is currently actionable (`MainMenu.cs` wires only `CoursesButton`).
- `CourseCatalog` is hardcoded for a single hole/course card and does not yet support multi-course discovery or progression.
- Marker UX currently has no dedicated clear-marker input, no save/restore across scene transitions, and no marker-specific tests.
- Tracked backlog: `docs/feature-gaps.md`.

## Addon Classes

| Class | Base | Description |
|-------|------|-------------|
| `BallPhysics` | `RefCounted` | Force, torque, and bounce calculations |
| `PhysicsParams` | `Resource` | Exported physics parameters |
| `BounceResult` | `RefCounted` | Bounce calculation result |
| `Aerodynamics` | `RefCounted` | Drag/lift coefficients, air density, viscosity |
| `Surface` | `RefCounted` | Surface parameter presets |
| `ShotSetup` | `RefCounted` | Spin parsing and launch vector utilities |
| `PhysicsAdapter` | `RefCounted` | Headless shot simulator |

All C# classes use `[GlobalClass]` for GDScript visibility. Enums are provided via `physics_enums.gd` (GDScript mirror of the C# `PhysicsEnums` definitions).

## Addon Structure

```
addons/openfairway/
├── plugin.cfg            Godot plugin metadata
├── plugin.gd             Plugin entry point
├── physics_enums.gd      GDScript enum mirror (BallState, Units, SurfaceType)
├── LICENSE               MIT license
├── README.md             Full GDScript API reference and examples
└── physics/
    ├── BallPhysics.cs    Force/torque/bounce calculations
    ├── PhysicsParams.cs  Physics parameters (Resource, exported)
    ├── BounceResult.cs   Bounce result data
    ├── Aerodynamics.cs   Cd/Cl coefficients, air properties
    ├── Surface.cs        Surface parameter presets
    ├── ShotSetup.cs      Spin parsing & launch vector utilities
    ├── PhysicsAdapter.cs Headless shot simulator
    ├── PhysicsEnums.cs   C# enum definitions (static class)
    └── README.md         Physics formulas and tuning guide
```

## Documentation

- **[Addon README](addons/openfairway/README.md)** — full GDScript API reference, installation details, complete physics loop and headless simulation examples
- **[Physics README](addons/openfairway/physics/README.md)** — force/torque formulas, bounce model, aerodynamic coefficients, unit conversions, tuning guide, and references

## Units Convention

The physics engine always uses SI internally (meters, m/s, rad/s). Launch monitor input uses Imperial (mph, degrees, RPM). Display conversion is the consumer's responsibility. See `ShotSetup.BuildLaunchVectors()` for the standard conversion path.

## License

MIT — see [LICENSE](addons/openfairway/LICENSE).
