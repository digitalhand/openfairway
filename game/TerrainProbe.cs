using Godot;
using Godot.Collections;

/// <summary>
/// Reusable terrain raycast utility.
/// Encapsulates the common pattern of casting a vertical ray through the physics world
/// and parsing the hit dictionary into a structured result.
/// </summary>
public static class TerrainProbe
{
    public readonly record struct TerrainHit(Vector3 Position, Vector3 Normal, Node Collider);

    /// <summary>
    /// Cast a vertical ray from (origin + Up * upOffset) to (origin + Down * downOffset).
    /// Returns null if no hit or world is unavailable.
    /// </summary>
    public static TerrainHit? Raycast(
        World3D world,
        Vector3 origin,
        float upOffset,
        float downOffset,
        Array<Rid> exclude)
    {
        if (world == null)
            return null;

        Vector3 rayStart = origin + Vector3.Up * upOffset;
        Vector3 rayEnd = origin + Vector3.Down * downOffset;

        var query = PhysicsRayQueryParameters3D.Create(rayStart, rayEnd);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.Exclude = exclude;

        var hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return null;

        Vector3 position = (Vector3)hit["position"];
        Vector3 normal = ((Vector3)hit["normal"]).Normalized();
        Node collider = hit.ContainsKey("collider") && hit["collider"].Obj is Node node
            ? node
            : null;
        if (normal.LengthSquared() < 0.000001f)
            normal = Vector3.Up;

        return new TerrainHit(position, normal, collider);
    }
}
