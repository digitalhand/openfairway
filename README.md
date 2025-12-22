# OpenFairway

OpenFairway is an open source golf game built with Godot 4.5 (.NET/C#).

## Table of Contents
- [Overview](#overview)
- [Features](#features)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Physics README](physics/README.md)

## Overview
OpenFairway focuses on realistic ball flight and rollout simulation with a range-style play loop. The core physics live in the `physics` folder and are shared by the in-game ball and the headless simulator. 
This game will focus on stylized visuals versus photorealistic visuals (e.g. GSPro). 

## Features
- Physics-based ball flight, bounce, and rollout
- Aerodynamics with drag and lift coefficient models
- Surface tuning for fairway, rough, soft, and firm conditions
- Range scene with UI input and optional TCP launch monitor payloads
- Phantom Camera integration for follow and reset behavior

## Project Structure
- `addons/` third-party plugins (including Phantom Camera)
- `courses/` scenes and controllers for range/course content
- `game/` gameplay nodes like the golf ball and shot tracker
- `physics/` ball physics, aerodynamics, and surface models
- `utils/` shared helpers, settings, and formatting

## Getting Started
1. Install Godot 4.5 with .NET support.
2. Open the project folder in Godot.
3. Run the main scene (already configured in `project.godot`).

## Physics Overview
The physics implementation is documented in `physics/README.md`.
