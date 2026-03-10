using System.Collections.Generic;
using Godot;

/// <summary>
/// Controller-owned lie surface resolver. It owns default surface selection,
/// active zone overrides, and GridMap mesh-library item parsing.
/// </summary>
public sealed class LieSurfaceResolver
{
    private readonly List<PhysicsEnums.SurfaceType> _zoneStack = new();
    private readonly List<GridMap> _surfaceGridMaps = new();

    public PhysicsEnums.SurfaceType DefaultSurface { get; private set; } = PhysicsEnums.SurfaceType.Fairway;
    public string LastResolutionSource { get; private set; } = "default";
    public string LastResolutionGridMapName { get; private set; } = string.Empty;
    public Vector3I LastResolutionCell { get; private set; } = Vector3I.Zero;
    public int RegisteredGridMapCount => _surfaceGridMaps.Count;

    public void SetDefaultSurface(PhysicsEnums.SurfaceType surface)
    {
        DefaultSurface = surface;
    }

    public void EnterZone(PhysicsEnums.SurfaceType surface)
    {
        _zoneStack.Add(surface);
    }

    public void ExitZone(PhysicsEnums.SurfaceType surface)
    {
        for (int i = _zoneStack.Count - 1; i >= 0; i--)
        {
            if (_zoneStack[i] != surface)
                continue;

            _zoneStack.RemoveAt(i);
            return;
        }
    }

    public void ClearZones()
    {
        _zoneStack.Clear();
    }

    public bool RegisterGridMap(GridMap gridMap)
    {
        if (gridMap == null || _surfaceGridMaps.Contains(gridMap))
            return false;

        if (!GridMapHasSurfaceLabels(gridMap))
            return false;

        _surfaceGridMaps.Add(gridMap);
        return true;
    }

    public void ClearGridMaps()
    {
        _surfaceGridMaps.Clear();
    }

    public PhysicsEnums.SurfaceType Resolve(Node collider, Vector3 worldPoint)
    {
        LastResolutionGridMapName = string.Empty;
        LastResolutionCell = Vector3I.Zero;

        if (_zoneStack.Count > 0)
        {
            LastResolutionSource = "zone";
            return _zoneStack[_zoneStack.Count - 1];
        }

        if (TryResolveFromRegisteredGridMaps(worldPoint, out var registeredSurface, out GridMap registeredGridMap, out Vector3I registeredCell))
        {
            LastResolutionSource = "gridmap_world_point";
            LastResolutionGridMapName = registeredGridMap?.Name ?? string.Empty;
            LastResolutionCell = registeredCell;
            return registeredSurface;
        }

        if (TryResolveFromCollider(collider, worldPoint, out var surface, out GridMap colliderGridMap, out Vector3I colliderCell))
        {
            LastResolutionSource = "collider_gridmap";
            LastResolutionGridMapName = colliderGridMap?.Name ?? string.Empty;
            LastResolutionCell = colliderCell;
            return surface;
        }

        LastResolutionSource = "default";
        return DefaultSurface;
    }

    public string DescribeLastResolution()
    {
        string baseSummary = $"source={LastResolutionSource}";
        if (!string.IsNullOrWhiteSpace(LastResolutionGridMapName))
            baseSummary += $" gridmap={LastResolutionGridMapName} cell={LastResolutionCell}";

        return $"{baseSummary} registered={RegisteredGridMapCount}";
    }

    public static bool TryParseMeshLibraryLabel(string label, out PhysicsEnums.SurfaceType surface)
    {
        surface = default;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        string token = label
            .Trim()
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');

        switch (token)
        {
            case "fairway":
                surface = PhysicsEnums.SurfaceType.Fairway;
                return true;

            case "rough":
                surface = PhysicsEnums.SurfaceType.Rough;
                return true;

            case "green":
                surface = PhysicsEnums.SurfaceType.Green;
                return true;

            default:
                return false;
        }
    }

    private bool TryResolveFromRegisteredGridMaps(
        Vector3 worldPoint,
        out PhysicsEnums.SurfaceType surface,
        out GridMap matchedGridMap,
        out Vector3I matchedCell)
    {
        surface = default;
        matchedGridMap = null;
        matchedCell = Vector3I.Zero;

        for (int i = _surfaceGridMaps.Count - 1; i >= 0; i--)
        {
            GridMap gridMap = _surfaceGridMaps[i];
            if (gridMap == null || !GodotObject.IsInstanceValid(gridMap))
            {
                _surfaceGridMaps.RemoveAt(i);
                continue;
            }

            if (TryResolveFromGridMap(gridMap, worldPoint, out surface, out matchedCell))
            {
                matchedGridMap = gridMap;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveFromCollider(
        Node collider,
        Vector3 worldPoint,
        out PhysicsEnums.SurfaceType surface,
        out GridMap matchedGridMap,
        out Vector3I matchedCell)
    {
        surface = default;
        matchedGridMap = FindGridMapFromCollider(collider);
        matchedCell = Vector3I.Zero;
        if (matchedGridMap == null)
            return false;

        return TryResolveFromGridMap(matchedGridMap, worldPoint, out surface, out matchedCell);
    }

    private static bool TryResolveFromGridMap(
        GridMap gridMap,
        Vector3 worldPoint,
        out PhysicsEnums.SurfaceType surface,
        out Vector3I matchedCell)
    {
        surface = default;
        matchedCell = Vector3I.Zero;
        if (gridMap == null || gridMap.MeshLibrary == null)
            return false;

        Vector3 localPoint = gridMap.ToLocal(worldPoint);
        Vector3I centerCell = gridMap.LocalToMap(localPoint);
        bool foundSurface = false;
        float bestDistanceSq = float.MaxValue;

        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3I candidateCell = centerCell + new Vector3I(x, y, z);
                    int itemId = gridMap.GetCellItem(candidateCell);
                    if (itemId < 0)
                        continue;

                    if (!TryParseMeshLibraryLabel(gridMap.MeshLibrary.GetItemName(itemId), out PhysicsEnums.SurfaceType candidateSurface))
                        continue;

                    float distanceSq = gridMap.MapToLocal(candidateCell).DistanceSquaredTo(localPoint);
                    if (foundSurface && distanceSq >= bestDistanceSq)
                        continue;

                    foundSurface = true;
                    bestDistanceSq = distanceSq;
                    matchedCell = candidateCell;
                    surface = candidateSurface;
                }
            }
        }

        return foundSurface;
    }

    private static bool GridMapHasSurfaceLabels(GridMap gridMap)
    {
        MeshLibrary meshLibrary = gridMap?.MeshLibrary;
        if (meshLibrary == null)
            return false;

        foreach (int itemId in meshLibrary.GetItemList())
        {
            if (TryParseMeshLibraryLabel(meshLibrary.GetItemName(itemId), out _))
                return true;
        }

        return false;
    }

    private static GridMap FindGridMapFromCollider(Node collider)
    {
        for (Node cursor = collider; cursor != null; cursor = cursor.GetParent())
        {
            if (cursor is GridMap gridMap)
                return gridMap;
        }

        return null;
    }
}
