using Godot;
using CorditeWars.Systems.FogOfWar;

namespace CorditeWars.Game.World;

/// <summary>
/// Renders the fog-of-war as a transparent overlay plane above the terrain.
/// Each game tick the simulation updates a <see cref="FogGrid"/>; this renderer
/// reads that grid each Godot frame and uploads a corresponding texture to a
/// large quad mesh spanning the entire map.
///
/// Three fog states map to visual alpha values:
/// <list type="bullet">
///   <item><see cref="FogVisibility.Unexplored"/> — nearly opaque black (α = 0.93).</item>
///   <item><see cref="FogVisibility.Explored"/>   — dim semi-transparent dark (α = 0.48).</item>
///   <item><see cref="FogVisibility.Visible"/>     — fully transparent (α = 0.00).</item>
/// </list>
/// </summary>
public partial class FogRenderer3D : Node3D
{
    // Fog alpha levels (0 = fully transparent, 1 = fully opaque)
    private const float AlphaUnexplored = 0.93f;
    private const float AlphaExplored   = 0.48f;
    private const float AlphaVisible    = 0.00f;

    // Height above the terrain mesh — just above the ground surface so the fog
    // sits clearly between the terrain and unit models.
    private const float FogPlaneY = 0.25f;

    private static readonly string FogShaderSource = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded;

uniform sampler2D fog_texture : hint_default_black, filter_linear;

void fragment() {
    vec4 fog = texture(fog_texture, UV);
    ALBEDO = vec3(0.0);
    ALPHA  = fog.r;
}
";

    private FogGrid?         _fogGrid;
    private int              _mapWidth;
    private int              _mapHeight;
    private MeshInstance3D?  _meshInstance;
    private ShaderMaterial?  _material;
    private Image?           _fogImage;
    private ImageTexture?    _fogTexture;
    // Pre-allocated byte buffer for bulk pixel upload — avoids per-cell SetPixel overhead.
    private byte[]?          _fogBytes;

    /// <summary>
    /// Initialises the renderer with the local player's fog grid and map dimensions.
    /// Must be called <em>after</em> the node has been added to the scene tree.
    /// </summary>
    /// <param name="fogGrid">
    /// The local player's <see cref="FogGrid"/>.  Pass <c>null</c> to create a
    /// no-op renderer (all cells visible) — used when fog of war is disabled.
    /// </param>
    /// <param name="mapWidth">Map width in grid cells.</param>
    /// <param name="mapHeight">Map height in grid cells.</param>
    public void Setup(FogGrid? fogGrid, int mapWidth, int mapHeight)
    {
        _fogGrid   = fogGrid;
        _mapWidth  = mapWidth;
        _mapHeight = mapHeight;

        // Remove any existing fog plane so repeated calls don't stack meshes.
        if (_meshInstance is not null && IsInstanceValid(_meshInstance))
        {
            _meshInstance.QueueFree();
            _meshInstance = null;
        }

        // Build the image: single-channel R8 with R used as opacity by the shader.
        _fogImage   = Image.CreateEmpty(mapWidth, mapHeight, false, Image.Format.R8);
        _fogBytes   = new byte[mapWidth * mapHeight]; // 1 byte per cell
        _fogTexture = ImageTexture.CreateFromImage(_fogImage);

        // Build the fog shader + material
        var shader = new Shader { Code = FogShaderSource };
        _material = new ShaderMaterial { Shader = shader };
        _material.SetShaderParameter("fog_texture", _fogTexture);

        // Plane mesh spanning the entire map (PlaneMesh is centred at origin)
        var planeMesh = new PlaneMesh();
        planeMesh.Size     = new Vector2(mapWidth, mapHeight);
        planeMesh.SubdivideDepth = 0;
        planeMesh.SubdivideWidth = 0;

        _meshInstance = new MeshInstance3D
        {
            Mesh             = planeMesh,
            MaterialOverride = _material,
            // Fog does not cast shadows
            CastShadow       = GeometryInstance3D.ShadowCastingSetting.Off
        };

        // Centre the plane over the map.  Map cells run from (0,0) to (W-1,H-1) in
        // world-space X/Z, so the plane centre is at (W/2, H/2).
        _meshInstance.Position = new Vector3(mapWidth  / 2f, FogPlaneY, mapHeight / 2f);

        AddChild(_meshInstance);

        // Fill with fully opaque black on first frame
        UploadFog();

        GD.Print($"[FogRenderer3D] Set up {mapWidth}x{mapHeight} fog plane " +
                 $"(fog grid = {(fogGrid == null ? "none" : $"player {fogGrid.PlayerId}")}).");
    }

    // ── Per-frame update ─────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_fogImage == null || _fogTexture == null)
            return;

        UploadFog();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads the current fog state and uploads the corresponding R8 image
    /// to the GPU texture.  Only the R channel is used (opacity).
    /// Uses a pre-allocated byte buffer for bulk upload to avoid per-cell
    /// <c>SetPixel</c> overhead on large maps.
    /// </summary>
    private void UploadFog()
    {
        if (_fogImage == null || _fogTexture == null || _fogBytes == null) return;

        if (_fogGrid == null)
        {
            // Fog disabled — fill fully transparent
            System.Array.Clear(_fogBytes, 0, _fogBytes.Length);
            _fogImage.SetData(_mapWidth, _mapHeight, false, Image.Format.R8, _fogBytes);
            _fogTexture.Update(_fogImage);
            return;
        }

        int w = _mapWidth;
        int h = _mapHeight;

        // Convert FogVisibility → byte opacity and write to flat buffer.
        // R8 layout is row-major: index = y * width + x.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte opacity = _fogGrid.Cells[x, y].Visibility switch
                {
                    FogVisibility.Visible    => (byte)(AlphaVisible    * 255f),
                    FogVisibility.Explored   => (byte)(AlphaExplored   * 255f),
                    _                        => (byte)(AlphaUnexplored * 255f)
                };
                _fogBytes[y * w + x] = opacity;
            }
        }

        _fogImage.SetData(w, h, false, Image.Format.R8, _fogBytes);
        _fogTexture.Update(_fogImage);
    }
}
