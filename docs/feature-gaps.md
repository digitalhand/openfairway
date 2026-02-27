# Feature Gaps Backlog

## Prioritization
- `P0`: blocks core gameplay or release.
- `P1`: high-impact gameplay/usability gap.
- `P2`: quality and maintainability gap.

## Gaps

### P1 - Main Menu "Range" Tile Is Not Actionable
- **Current state:** `ui/main_menu.tscn` has `RangeTile`, but `ui/MainMenu.cs` only wires `CoursesButton`.
- **Acceptance criteria:**
  - Clicking `RangeTile` or `RangeButton` changes to a valid scene.
  - Failed scene loads emit a clear error message.
  - Tile labels and behavior match actual destinations.
- **Likely files:** `ui/MainMenu.cs`, `ui/main_menu.tscn`.

### P1 - Course Catalog Is Single-Course/Single-Hole
- **Current state:** `game/scoring/CourseCatalog.cs` contains only one hardcoded entry (`hole_1`).
- **Acceptance criteria:**
  - Catalog supports multiple holes/courses with stable keys.
  - UI can display metadata for selected hole/course.
  - Unknown scene IDs degrade gracefully to defaults.
- **Likely files:** `game/scoring/CourseCatalog.cs`, `ui/MainMenu.cs`, `ui/CourseHud.cs`.

### P1 - Marker UX Missing Explicit Clear/Persist Flows
- **Current state:** player marker is set by click and cleared on launch/reset; no dedicated clear action or persistence.
- **Acceptance criteria:**
  - Add explicit "clear marker" input/UI affordance.
  - Marker persistence behavior is defined (persist or intentionally reset) across scene/menu transitions.
  - Behavior is documented in README controls.
- **Likely files:** `game/markers/ShotMarkerController.cs`, `game/camera/ShotCameraController.cs`, `courses/airways_fresno/hole_1/Hole1.cs`, `ui/GameplayUI.cs`.

### P2 - Marker-Specific Automated Tests
- **Current state:** no focused unit tests for marker snapshot transitions.
- **Acceptance criteria:**
  - Tests cover rest/launch/reset/countdown visibility rules.
  - Tests cover click-to-selection and suppression behavior.
  - Snapshot equality/changed-publication behavior is asserted.
- **Likely files:** `tests/PhysicsTests/` (new marker-focused test file), `game/markers/MarkerSnapshot.cs`, `game/markers/ShotMarkerController.cs`.
