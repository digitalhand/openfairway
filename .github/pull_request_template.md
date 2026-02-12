## Description
<!-- Provide a clear and concise description of your changes -->

## Type of Change
<!-- Mark the relevant option with an "x" -->

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Physics tuning (changes to rollout/aerodynamic parameters)
- [ ] Documentation update
- [ ] Test improvements

## Physics Changes (if applicable)
<!-- If you modified physics behavior, document the changes -->

### Modified Parameters
- [ ] Velocity scaling curve
- [ ] Spin multiplier curve
- [ ] COR (Coefficient of Restitution)
- [ ] Surface friction coefficients
- [ ] Aerodynamic coefficients (Cd/Cl)

### Test Results
<!-- Run test shots and compare to baselines -->
```
Chip shot:    X.X / X.X yd  (baseline: 7.6/13.1 yd, target: 13.0 yd)
Bump shot:    X.X / X.X yd  (baseline: 38.1/89.7 yd, target: 85 yd)
Wood shot:    X.X / X.X yd  (baseline: 121.7/194.7 yd, target: 198 yd)
Approach shot: X.X / X.X yd  (baseline: 105.6/108.3 yd, target: 108 yd)
```

### Formula Tests
- [ ] Rollout physics tests passing: `dotnet test --filter "Category=RolloutPhysics"`
- [ ] Updated test expected values (if formulas changed)
- [ ] Updated regression baselines in `ShotDistanceRegressionTests.cs`

## Testing Checklist
<!-- Mark completed items with an "x" -->

- [ ] All tests pass locally: `dotnet test OpenShotGolf.sln`
- [ ] Rollout physics formula tests pass: `dotnet test --filter "Category=RolloutPhysics"`
- [ ] Code builds without errors: `dotnet build OpenShotGolf.sln`
- [ ] Tested in Godot editor (if applicable)
- [ ] Manual testing performed (describe in comments below)

## Code Quality
- [ ] Code follows project conventions (PascalCase for public, _camelCase for private)
- [ ] Added/updated XML documentation comments for public APIs
- [ ] No compiler warnings introduced
- [ ] Code formatted with `dotnet format` (will be checked by CI)

## Additional Notes
<!-- Any additional context, screenshots, or notes for reviewers -->

## Related Issues
<!-- Link to related issues using #issue_number -->
Closes #
