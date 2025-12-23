using Godot;
using Godot.Collections;

/// <summary>
/// Visual trail/tracer for golf ball flight path.
/// Creates a camera-facing ribbon mesh that follows the ball's trajectory.
/// </summary>
public partial class BallTrail : MeshInstance3D
{
    [Export] public Color Color { get; set; } = new Color(0.153f, 0.408f, 0.663f, 0.6f);  // Trail color (light blue default)
    [Export] public float LineWidth { get; set; } = 0.08f;  // Width of the trail ribbon

    private Vector3[] _points = System.Array.Empty<Vector3>();
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        Mesh = new ArrayMesh();
        SetupMaterial();
    }

    private void SetupMaterial()
    {
        _material = new StandardMaterial3D();
        _material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _material.AlbedoColor = Color;
        _material.EmissionEnabled = true;
        _material.Emission = Color;
        _material.EmissionEnergyMultiplier = 0.5f;
        _material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _material.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
        _material.DisableReceiveShadows = true;
        _material.NoDepthTest = false;
    }

    public override void _Process(double delta)
    {
        DrawTrail();
    }

    /// <summary>
    /// Set the trail color
    /// </summary>
    public void SetColor(Color newColor)
    {
        Color = newColor;
        if (_material != null)
        {
            _material.AlbedoColor = Color;
            _material.Emission = Color;
        }
    }

    /// <summary>
    /// Add a point to the trail
    /// </summary>
    public void AddPoint(Vector3 point)
    {
        var newPoints = new Vector3[_points.Length + 1];
        _points.CopyTo(newPoints, 0);
        newPoints[_points.Length] = point;
        _points = newPoints;
    }

    /// <summary>
    /// Clear all points from the trail
    /// </summary>
    public void ClearPoints()
    {
        _points = System.Array.Empty<Vector3>();
        if (Mesh != null)
        {
            ((ArrayMesh)Mesh).ClearSurfaces();
        }
    }

    private void DrawTrail()
    {
        var arrayMesh = (ArrayMesh)Mesh;
        arrayMesh.ClearSurfaces();

        if (_points.Length < 2)
            return;

        CreateRibbonMesh(arrayMesh);
    }

    private void CreateRibbonMesh(ArrayMesh arrayMesh)
    {
        var vertices = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var colors = new System.Collections.Generic.List<Color>();
        var indices = new System.Collections.Generic.List<int>();

        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;

        for (int i = 0; i < _points.Length; i++)
        {
            Vector3 point = _points[i];

            // Billboard direction to camera
            Vector3 toCamera = (camera.GlobalPosition - point).Normalized();

            // Forward direction along the path
            Vector3 forward = GetForwardDirection(i);

            // Perpendicular vector for ribbon width
            Vector3 right = toCamera.Cross(forward).Normalized();
            if (right.Length() < 0.01f)
                right = Vector3.Right;

            // Fade out towards the end
            float alpha = CalculateAlpha(i);

            // Create two vertices (left and right of center)
            float halfWidth = LineWidth * 0.5f;
            vertices.Add(point - right * halfWidth);
            vertices.Add(point + right * halfWidth);

            // UVs
            float t = (float)i / (_points.Length - 1);
            uvs.Add(new Vector2(0, t));
            uvs.Add(new Vector2(1, t));

            // Colors with alpha
            var vertexColor = new Color(Color.R, Color.G, Color.B, alpha * Color.A);
            colors.Add(vertexColor);
            colors.Add(vertexColor);

            // Triangle indices (connecting to previous segment)
            if (i > 0)
            {
                int baseIdx = i * 2;
                // First triangle
                indices.Add(baseIdx);
                indices.Add(baseIdx - 2);
                indices.Add(baseIdx - 1);
                // Second triangle
                indices.Add(baseIdx - 1);
                indices.Add(baseIdx + 1);
                indices.Add(baseIdx);
            }
        }

        // Build the mesh
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        arrayMesh.SurfaceSetMaterial(0, _material);
    }

    private Vector3 GetForwardDirection(int index)
    {
        if (index < _points.Length - 1)
        {
            return (_points[index + 1] - _points[index]).Normalized();
        }
        else if (index > 0)
        {
            return (_points[index] - _points[index - 1]).Normalized();
        }
        else
        {
            return Vector3.Forward;
        }
    }

    private float CalculateAlpha(int index)
    {
        int pointsFromEnd = _points.Length - 1 - index;
        if (pointsFromEnd < 3)
        {
            return (float)(pointsFromEnd + 1) / 4.0f;
        }
        return 1.0f;
    }
}
