using Godot;
using NUnit.Framework;

namespace PhysicsTests;

[TestFixture]
public class LieSurfaceResolverTests
{
    [TestCase("Fairway", PhysicsEnums.SurfaceType.Fairway)]
    [TestCase("Green", PhysicsEnums.SurfaceType.Green)]
    [TestCase("Rough", PhysicsEnums.SurfaceType.Rough)]
    [TestCase(" fairway ", PhysicsEnums.SurfaceType.Fairway)]
    public void TryParseMeshLibraryLabel_ParsesSupportedLabels(string label, PhysicsEnums.SurfaceType expected)
    {
        bool parsed = LieSurfaceResolver.TryParseMeshLibraryLabel(label, out var surface);

        Assert.That(parsed, Is.True);
        Assert.That(surface, Is.EqualTo(expected));
    }

    [Test]
    public void TryParseMeshLibraryLabel_RejectsUnsupportedLegacyLabel()
    {
        bool parsed = LieSurfaceResolver.TryParseMeshLibraryLabel("surface:green", out _);

        Assert.That(parsed, Is.False);
    }

    [Test]
    public void TryParseMeshLibraryLabel_RejectsUnsupportedSurfaceTypeName()
    {
        bool parsed = LieSurfaceResolver.TryParseMeshLibraryLabel("Firm", out _);

        Assert.That(parsed, Is.False);
    }

    [Test]
    public void Resolve_UsesDefaultSurfaceWhenNoOverridesExist()
    {
        var resolver = new LieSurfaceResolver();
        resolver.SetDefaultSurface(PhysicsEnums.SurfaceType.Firm);

        PhysicsEnums.SurfaceType surface = resolver.Resolve(null, Vector3.Zero);

        Assert.That(surface, Is.EqualTo(PhysicsEnums.SurfaceType.Firm));
    }

    [Test]
    public void Resolve_UsesZoneOverrideBeforeDefault()
    {
        var resolver = new LieSurfaceResolver();
        resolver.SetDefaultSurface(PhysicsEnums.SurfaceType.Fairway);

        resolver.EnterZone(PhysicsEnums.SurfaceType.Green);
        Assert.That(resolver.Resolve(null, Vector3.Zero), Is.EqualTo(PhysicsEnums.SurfaceType.Green));

        resolver.ExitZone(PhysicsEnums.SurfaceType.Green);
        Assert.That(resolver.Resolve(null, Vector3.Zero), Is.EqualTo(PhysicsEnums.SurfaceType.Fairway));
    }

}
