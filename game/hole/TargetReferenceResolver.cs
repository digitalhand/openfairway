using System;
using Godot;
using Godot.Collections;

public sealed class TargetReferenceResolver
{
    private readonly GolfBall _ball;
    private readonly Camera3D _camera;
    private readonly Func<World3D> _worldProvider;
    private readonly Func<GodotObject> _terrainDataProvider;
    private readonly Func<Vector3?> _distanceReferencePointProvider;
    private readonly float _clickRayDistance;

    public TargetReferenceResolver(
        GolfBall ball,
        Camera3D camera,
        Func<World3D> worldProvider,
        Func<GodotObject> terrainDataProvider,
        Func<Vector3?> distanceReferencePointProvider,
        float clickRayDistance)
    {
        _ball = ball;
        _camera = camera;
        _worldProvider = worldProvider;
        _terrainDataProvider = terrainDataProvider;
        _distanceReferencePointProvider = distanceReferencePointProvider;
        _clickRayDistance = clickRayDistance;
    }

    public bool TryGetDistanceReferencePoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.Zero;
        Vector3? point = _distanceReferencePointProvider?.Invoke();
        if (!point.HasValue)
            return false;

        worldPoint = point.Value;
        return true;
    }

    public bool TryGetTargetDistanceText(out string distanceText)
    {
        distanceText = string.Empty;
        if (_ball == null || !TryGetDistanceReferencePoint(out Vector3 referencePoint))
            return false;

        distanceText = MeasurementUtils.FormatHorizontalDistanceShortAware(_ball.GlobalPosition, referencePoint);
        return true;
    }

    public bool TryGetTargetElevationFeet(out int feet)
    {
        feet = 0;
        if (_ball == null || !TryGetDistanceReferencePoint(out Vector3 referencePoint))
            return false;

        feet = MeasurementUtils.VerticalDeltaFeet(_ball.GlobalPosition, referencePoint);
        return true;
    }

    public Vector3 SnapPointToTerrain(Vector3 worldPoint)
    {
        if (TrySampleTerrainHeight(worldPoint, out float terrainHeight))
            return new Vector3(worldPoint.X, terrainHeight, worldPoint.Z);

        return worldPoint;
    }

    public bool TrySampleTerrainHeight(Vector3 worldPoint, out float terrainHeight)
    {
        terrainHeight = 0.0f;
        GodotObject terrainData = _terrainDataProvider?.Invoke();
        if (terrainData == null)
            return false;

        float height = (float)terrainData.Call("get_height", worldPoint);
        if (float.IsNaN(height))
            return false;

        terrainHeight = height;
        return true;
    }

    public bool TryGetWorldClickPoint(Vector2 mousePosition, out Vector3 worldPoint)
    {
        worldPoint = Vector3.Zero;
        if (_camera == null)
            return false;

        World3D world = _worldProvider?.Invoke();
        if (world == null)
            return false;

        Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePosition);
        Vector3 rayDirection = _camera.ProjectRayNormal(mousePosition);
        var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayOrigin + rayDirection * _clickRayDistance);
        query.CollideWithBodies = true;
        query.CollideWithAreas = true;

        if (_ball != null)
        {
            var exclude = new Array<Rid>();
            exclude.Add(_ball.GetRid());
            query.Exclude = exclude;
        }

        Dictionary hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0 && hit.ContainsKey("position"))
        {
            worldPoint = (Vector3)hit["position"];
            return true;
        }

        return TryResolveTerrainPointFromRay(mousePosition, out worldPoint);
    }

    private bool TryResolveTerrainPointFromRay(Vector2 mousePosition, out Vector3 worldPoint)
    {
        worldPoint = Vector3.Zero;
        if (_camera == null || _ball == null)
            return false;

        GodotObject terrainData = _terrainDataProvider?.Invoke();
        if (terrainData == null)
            return false;

        Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePosition);
        Vector3 rayDirection = _camera.ProjectRayNormal(mousePosition);
        if (Mathf.Abs(rayDirection.Y) < 0.00001f)
            return false;

        float t = (_ball.GlobalPosition.Y - rayOrigin.Y) / rayDirection.Y;
        if (t <= 0.0f)
            return false;

        Vector3 planePoint = rayOrigin + rayDirection * t;
        float height = (float)terrainData.Call("get_height", planePoint);
        if (float.IsNaN(height))
            return false;

        worldPoint = new Vector3(planePoint.X, height, planePoint.Z);
        return true;
    }
}
