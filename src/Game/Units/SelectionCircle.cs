using Godot;

namespace CorditeWars.Game.Units;

/// <summary>
/// Flat ring mesh on the ground plane visualizing a unit's collision radius
/// and selection state. The ring radius matches CollisionRadius from the
/// asset manifest so selection visual = collision bounds.
/// </summary>
public partial class SelectionCircle : MeshInstance3D
{
    private const int RingSegments = 32;
    private const float RingThickness = 0.05f;
    private const float YOffset = 0.05f;

    private StandardMaterial3D? _material;

    /// <summary>
    /// Creates the ring mesh matching the given collision radius.
    /// </summary>
    public void Initialize(float radius)
    {
        _material = new StandardMaterial3D();
        _material.AlbedoColor = new Color(0.0f, 1.0f, 0.0f, 0.8f);
        _material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _material.NoDepthTest = true;
        _material.RenderPriority = 1;

        Mesh = BuildRingMesh(radius);
        MaterialOverride = _material;

        // Position slightly above ground to avoid z-fighting
        Position = new Vector3(0.0f, YOffset, 0.0f);

        Visible = false;
    }

    /// <summary>
    /// Show or hide the selection ring.
    /// </summary>
    public void SetSelected(bool selected)
    {
        Visible = selected;
    }

    /// <summary>
    /// Change the ring color (green for friendly selected, red for enemy, etc.).
    /// </summary>
    public void SetColor(Color color)
    {
        if (_material is not null)
        {
            _material.AlbedoColor = new Color(color.R, color.G, color.B, 0.8f);
        }
    }

    private static Mesh BuildRingMesh(float radius)
    {
        var mesh = new ImmediateMesh();

        float outerRadius = radius;
        float innerRadius = radius - RingThickness;
        if (innerRadius < 0.01f)
            innerRadius = 0.01f;

        mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);

        for (int i = 0; i <= RingSegments; i++)
        {
            float angle = (float)i / RingSegments * Mathf.Tau;
            float cosA = Mathf.Cos(angle);
            float sinA = Mathf.Sin(angle);

            mesh.SurfaceSetNormal(Vector3.Up);

            // Outer vertex
            mesh.SurfaceAddVertex(new Vector3(cosA * outerRadius, 0.0f, sinA * outerRadius));

            // Inner vertex
            mesh.SurfaceAddVertex(new Vector3(cosA * innerRadius, 0.0f, sinA * innerRadius));
        }

        mesh.SurfaceEnd();

        return mesh;
    }
}
