# GitHub Actions Workflows

This directory contains automated CI/CD workflows for the OpenFairway project.

## Workflows

### 1. `ci.yml` - Continuous Integration Pipeline

**Triggers:** Pull requests and pushes to `main` branch

**Jobs:**

#### Build & Test
- Builds the .NET solution
- Runs rollout physics formula validation tests
- Runs all unit tests
- Uploads test results as artifacts

#### Code Quality Checks
- Verifies C# code formatting with `dotnet format`
- Runs .NET code analyzers
- Enforces code style consistency

#### GDScript Linting
- Runs `gdlint` on project GDScript files
- Excludes third-party addon files (PhantomCamera)
- Warnings don't fail the build (informational only)

#### Physics Formula Validation
- **Critical check** - Validates rollout physics formulas
- Ensures friction multipliers match expected values:
  - Chip (2785 RPM @ 2.44 m/s): ×1.95
  - Bump (1365 RPM @ 9.25 m/s): ×1.37
  - Driver (1118 RPM @ 16 m/s): ×1.20-1.30
- Detects unintended physics regressions

#### Security Analysis
- Runs CodeQL security scanning
- Detects potential vulnerabilities in C# code
- Required for production releases

#### CI Summary
- Aggregates results from all jobs
- Provides clear pass/fail status
- Only passes if all critical checks succeed

**Status:** Required for PR merge

---

### 2. `release.yml` - Automated Releases

**Triggers:** Git tags matching `v*.*.*` (e.g., `v1.0.0`)

**Jobs:**

#### Create Release
- Builds the solution in Release configuration
- Runs all tests to validate release
- Packages the `addons/openfairway/` directory
- Creates a GitHub release with:
  - Zipped addon package
  - Auto-generated release notes
  - Validated shot distance baselines
  - Installation instructions

**Usage:**
```bash
git tag v1.0.0
git push origin v1.0.0
```

---

## Local Development Workflow

### Before Creating a PR

1. **Run local tests:**
   ```bash
   dotnet test OpenShotGolf.sln
   ```

2. **Validate rollout physics formulas:**
   ```bash
   dotnet test --filter "Category=RolloutPhysics"
   ```

3. **Check code formatting:**
   ```bash
   dotnet format OpenShotGolf.sln --verify-no-changes
   ```

4. **Fix formatting issues:**
   ```bash
   dotnet format OpenShotGolf.sln
   ```

5. **Build in Release mode:**
   ```bash
   dotnet build OpenShotGolf.sln --configuration Release
   ```

### Making Physics Changes

If you modified physics behavior:

1. **Run rollout physics tests** - They may fail if formulas changed
2. **Test in Godot** - Validate shot distances match expectations
3. **Update test baselines** - Modify expected values in `RolloutPhysicsTests.cs`
4. **Document changes** - Update PR template with test results
5. **Update regression baselines** - Modify `ShotDistanceRegressionTests.cs` if needed

See `tests/PhysicsTests/README.md` for detailed testing workflow.

---

## CI Status Badges

Add to your `README.md`:

```markdown
![CI Status](https://github.com/digitalhand/openfairway/workflows/CI%2FCD%20Pipeline/badge.svg)
![Release](https://github.com/digitalhand/openfairway/workflows/Release/badge.svg)
```

---

## Troubleshooting

### "dotnet format verification failed"

Your code doesn't match the formatting rules in `.editorconfig`. Fix with:
```bash
dotnet format OpenShotGolf.sln
git add .
git commit --amend --no-edit
git push --force
```

### "Rollout physics tests failed"

You modified physics formulas. Either:
- **Revert the changes** if unintended
- **Update the tests** with new expected values if intentional
- See `tests/PhysicsTests/RolloutPhysicsTests.cs`

### "CodeQL analysis failed"

Security scan detected potential issues. Review the security alerts in the GitHub Security tab and address vulnerabilities.

### "GDScript linting warnings"

GDScript linting is informational only and won't block merges. Fix warnings with:
```bash
gdformat <file.gd>
```

---

## Workflow Configuration

### Required Secrets

None required for CI/CD (uses default `GITHUB_TOKEN`).

### Required Permissions

- **Contents:** write (for releases)
- **Security-events:** write (for CodeQL)

### Branch Protection Rules (Recommended)

Enable on `main` branch:
- ✅ Require status checks to pass:
  - `Build & Test`
  - `Code Quality Checks`
  - `Physics Formula Validation`
- ✅ Require branches to be up to date before merging
- ✅ Require linear history (optional)

---

## Future Enhancements

Potential workflow additions:

- [ ] Benchmark performance tests (shot simulation speed)
- [ ] Godot editor integration tests (requires Godot Docker container)
- [ ] Automated documentation generation from XML comments
- [ ] Code coverage reporting with Codecov
- [ ] Dependency vulnerability scanning with Dependabot
