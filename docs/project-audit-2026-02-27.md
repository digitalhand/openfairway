# Project Audit - 2026-02-27

## Scope
- First-party OpenFairway project only:
  - `README.md`, `CLAUDE.md`, `tests/PhysicsTests/README.md`
  - `courses/`, `game/`, `ui/`, `utils/`, `addons/openfairway/`
- Excluded vendored third-party addons.

## Audit Summary
This audit focused on documentation freshness, PlantUML accuracy, and feature coverage clarity. The highest-impact drift was architecture docs still describing a `Range` controller flow while the runtime flow is `Hole1`-centric with dedicated camera and marker controllers.

## Findings

### 1. Stale Scene/Controller References in README
- **Evidence:** `README.md` referenced `courses/Range/Range.cs` and `Range` in the architecture diagram.
- **Impact:** Misleads contributors about entry points and ownership boundaries.
- **Remediation:** Updated README to reference `courses/airways_fresno/hole_1/Hole1.cs`, `ShotCameraController`, and `ShotMarkerController`.

### 2. PlantUML Diagram Drift
- **Evidence:** Diagram modeled a legacy `Range` controller-to-UI flow and omitted the current marker/camera/controller flow.
- **Impact:** Onboarding and plan reviews use outdated architecture.
- **Remediation:** Replaced diagram with current class relationships (`Hole1`, `ShotCameraController`, `ShotMarkerController`, `GameplayUI`, `CourseHud`, `MarkerHUD`).

### 3. Benchmark Baseline Inconsistencies
- **Evidence:** `tests/PhysicsTests/README.md` used 2026-02-18 baselines; `RolloutPhysicsTests.cs` historical comments still used 2024 values.
- **Impact:** Confusion on which baseline values are authoritative.
- **Remediation:** Updated historical baseline comments and aligned test README workflow guidance.

### 4. Marker Feature Underdocumented
- **Evidence:** Marker behavior existed in code (`Hole1`, `ShotCameraController`, `ShotMarkerController`, `MarkerHUD`) but was not explicitly documented as user controls.
- **Impact:** Contributors cannot reliably validate marker UX expectations.
- **Remediation:** Added explicit marker control section to README.

## Open Feature Gaps
See [feature-gaps.md](feature-gaps.md) for prioritized gaps and acceptance criteria.
