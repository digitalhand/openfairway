# Physics

This folder contains the ball flight and rollout simulation used by the game and the headless simulator.

## Table of Contents
- [Overview](#overview)
- [Key Modules](#key-modules)
- [Data Flow](#data-flow)
- [Core Calculations](#core-calculations)
- [Bounce and Rollout](#bounce-and-rollout)
- [Units and Conventions](#units-and-conventions)
- [Tuning](#tuning)

## Overview
The physics stack models forces (gravity, air, and ground), torques, bounce response, and surface interactions. It is designed to be called from both the in-game `GolfBall` node and the headless `PhysicsAdapter` simulator.

## Key Modules
- `BallPhysics.cs` core force, torque, and bounce calculations
- `Aerodynamics.cs` drag/lift coefficients plus air density/viscosity helpers
- `Surface.cs` surface parameter presets (friction, grass drag, critical angle)
- `PhysicsAdapter.cs` headless simulation for JSON shot inputs
- `PhysicsEnums.cs` shared enums for ball state, units, and surface types

## Data Flow
1. Build a `BallPhysics.PhysicsParams` from environment and surface values.
2. Each step computes total forces and torques.
3. Integrate velocity and angular velocity.
4. On impact, call `BallPhysics.CalculateBounce` to update velocity, spin, and state.

## Core Calculations
### Forces
- Gravity: `g = (0, -9.81 * mass, 0)`
- Drag: `Fd = -0.5 * Cd * rho * A * v * |v|`
- Magnus: `Fm = 0.5 * Cl * rho * A * (omega x v) * |v| / |omega|` (when |omega| > 0.1)
- Grass drag (ground only): `Fgrass = -6 * pi * R * nu_g * v` (horizontal only)

Drag and lift coefficients are derived from Reynolds number and spin ratio:
- `Re = rho * |v| * (2 * R) / mu`
- `S = |omega| * R / |v|`

### Ground Friction Model
Contact point velocity uses the surface normal `n`:
- `v_contact = v + omega x (-n * R)`
- `v_tangent = v_contact - n * (v_contact dot n)`

Friction force is split into two regimes:
- Rolling: `|v_tangent| < 0.05`, use rolling resistance `F = -c_rr * m * g` along flat velocity
- Slipping: use blended friction based on speed (0 to 15 m/s), `F = -mu_eff * m * g` along slip

### Torques
- Air spin decay: `tau = -I * omega / SPIN_DECAY_TAU` (in flight)
- Ground torque from friction: `T = (-n * R) x F_friction`
- Grass torque: `T_grass = -6 * pi * nu_g * R * omega`

## Bounce and Rollout
The bounce response decomposes velocity and spin into normal/tangent components, then applies tangential retention and COR.

### Tangential Retention
- First bounce (flight): retention depends on spin magnitude
- Rollout bounces: retention depends on spin ratio

### Tangential Speed Update
- Shallow impact: `v_tangent_new = v_tangent * retention`
- Steep impact: Penner model: `v_tangent_new = retention * |v| * sin(impactAngle - criticalAngle) - 2 * R * |omega_tangent| / 7`

### Spin Update
- First bounce: `omega_tangent` is limited to `v_tangent_new / R`
- Rollout: spin axis is preserved and reoriented toward rolling direction

### Normal Response (COR)
- `COR(speedNormal)` uses a speed-dependent curve
- Rollout bounces reduce COR and kill small normal speeds

## Units and Conventions
- Distances in meters, speeds in meters/second.
- Angular velocity uses radians/second (`omega`).
- Angles are in radians unless explicitly converted for logging.
- Spin debug output converts rad/s to RPM via `omega / 0.10472`.

## Tuning
- Adjust surface behavior in `Surface.cs`.
- Change physical constants (mass, radius, spin decay) in `BallPhysics.cs`.
- Swap parameters by passing different `PhysicsParams` from `GolfBall` or `PhysicsAdapter`.
