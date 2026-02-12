# Contributing to OpenFairway

Thank you for your interest in contributing to OpenFairway! This guide will help you get started.

## Quick Start

1. **Fork the repository**
2. **Clone your fork:**
   ```bash
   git clone https://github.com/YOUR-USERNAME/openfairway.git
   cd openfairway
   ```
3. **Install dependencies:**
   - Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
   - Install [Godot 4.5+ (.NET build)](https://godotengine.org/download)
4. **Build the project:**
   ```bash
   dotnet build OpenShotGolf.sln
   ```
5. **Run tests:**
   ```bash
   dotnet test OpenShotGolf.sln
   ```

## Development Workflow

### Creating a Pull Request

1. **Create a feature branch:**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes** and commit:
   ```bash
   git add .
   git commit -m "feat: add awesome feature"
   ```

3. **Run pre-PR checks:**
   ```bash
   # Build
   dotnet build OpenShotGolf.sln --configuration Release

   # Run all tests
   dotnet test OpenShotGolf.sln

   # Validate rollout physics formulas
   dotnet test --filter "Category=RolloutPhysics"

   # Check code formatting
   dotnet format OpenShotGolf.sln --verify-no-changes
   ```

4. **Push your branch:**
   ```bash
   git push origin feature/your-feature-name
   ```

5. **Create a Pull Request** on GitHub

### Commit Message Convention

Use conventional commits format:

- `feat:` - New feature
- `fix:` - Bug fix
- `docs:` - Documentation changes
- `test:` - Test additions/changes
- `refactor:` - Code refactoring
- `perf:` - Performance improvements
- `style:` - Code style changes (formatting)
- `chore:` - Build/tooling changes

Examples:
```
feat: add velocity-dependent friction scaling
fix: correct COR calculation for low-speed impacts
docs: update rollout physics documentation
test: add regression tests for bump shot rollout
```

## Physics Changes

If you're modifying physics behavior, please:

### 1. Understand the Impact

Read the physics documentation:
- `addons/openfairway/physics/README.md` - Physics formulas
- `tests/PhysicsTests/README.md` - Testing workflow
- `CLAUDE.md` - Architecture and debugging tips

### 2. Test Thoroughly

```bash
# Run rollout physics formula validation
dotnet test --filter "Category=RolloutPhysics"

# Test in Godot with all shot types:
# - chip_test_shot.json
# - bump_test_shot.json
# - wood_low_test_shot.json
# - approach_test_shot.json
# - flop_test_shot.json
```

### 3. Document Results

In your PR, include:
- What physics parameters changed
- Before/after shot distances for all test shots
- Whether formula tests needed updating
- Justification for the change (e.g., "matches real-world data")

### 4. Update Tests

If you intentionally changed physics formulas:

1. Update expected values in `tests/PhysicsTests/RolloutPhysicsTests.cs`
2. Update baselines in `tests/PhysicsTests/ShotDistanceRegressionTests.cs`
3. Document the changes in your PR

## Code Style

### C# Guidelines

- **Naming:**
  - PascalCase for public members
  - _camelCase for private fields
  - UPPER_CASE for constants
- **Documentation:**
  - Add XML comments for all public APIs
  - Include `<summary>`, `<param>`, `<returns>` tags
- **Formatting:**
  - Use `.editorconfig` settings (enforced by CI)
  - Run `dotnet format` before committing

Example:
```csharp
/// <summary>
/// Calculates the friction multiplier based on spin and velocity.
/// </summary>
/// <param name="spinRpm">Impact spin rate in RPM</param>
/// <param name="velocity">Ball velocity in m/s</param>
/// <returns>Friction multiplier (1.0 = baseline)</returns>
private float CalculateFrictionMultiplier(float spinRpm, float velocity)
{
    // Implementation
}
```

### GDScript Guidelines

- Use tabs for indentation (Godot convention)
- Follow [GDScript style guide](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/gdscript_styleguide.html)
- Run `gdlint` on modified files (informational, not required)

## Testing

### Running Tests Locally

```bash
# All tests
dotnet test OpenShotGolf.sln

# Rollout physics formula validation (fast, no Godot needed)
dotnet test --filter "Category=RolloutPhysics"

# Specific test class
dotnet test --filter FullyQualifiedName~RolloutPhysicsTests

# With detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Writing New Tests

For physics formula changes:

1. Add test to `RolloutPhysicsTests.cs`:
   ```csharp
   [Test]
   [Category("RolloutPhysics")]
   public void MyNewPhysicsFormula_IsCorrect()
   {
       float result = CalculateMyFormula(input);
       Assert.That(result, Is.EqualTo(expected).Within(0.01f));
   }
   ```

2. Add regression baseline to `ShotDistanceRegressionTests.cs` (if adding new shot type)

## CI/CD Pipeline

All PRs must pass CI checks:

- ✅ **Build & Test** - Solution compiles, all tests pass
- ✅ **Code Quality** - Code formatting matches `.editorconfig`
- ✅ **Physics Formulas** - Rollout physics tests validate
- ℹ️ **GDScript Lint** - Informational warnings (won't block merge)
- ✅ **Security Scan** - CodeQL analysis passes

See `.github/WORKFLOWS.md` for detailed CI documentation.

## Getting Help

- **Documentation:** Start with `CLAUDE.md` and `README.md`
- **Physics Questions:** See `addons/openfairway/physics/README.md`
- **Testing Help:** See `tests/PhysicsTests/README.md`
- **Issues:** [GitHub Issues](https://github.com/digitalhand/openfairway/issues)

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
