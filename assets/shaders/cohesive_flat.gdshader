// =============================================================================
// cohesive_flat.gdshader
// =============================================================================
// Asset Cohesion Pipeline — Runtime Cohesion Shader (Godot 4.x)
//
// PURPOSE:
//   This is the final visual cohesion layer applied at runtime. By the time a
//   mesh reaches this shader, the Blender pipeline has already:
//     • Normalized geometry scale and orientation
//     • Baked and normalized vertex colors to a shared palette
//     • Reduced material variety to a predictable set
//
//   This shader's job is to guarantee that *every* asset in the game — regardless
//   of origin — renders with the same lighting response, banding style, rim
//   treatment, and noise grain. Visual cohesion is enforced here, not hoped for.
//
// DESIGN PHILOSOPHY:
//   - Cel-shaded banding gives all assets the same hand-painted feel
//   - Rim lighting ensures units "pop" against terrain in an RTS top-down view
//   - Procedural noise adds micro-variation so flat colors don't feel digital
//   - Optional palette enforcement snaps any residual color drift at runtime
//   - Team color tinting is done here so faction coloring is always consistent
//
// GODOT 4 NOTES:
//   - Uses the light() function with DIFFUSE_LIGHT / SPECULAR_LIGHT outputs
//   - Palette loop uses a compile-time constant MAX_PALETTE_SIZE; textures larger
//     than that are silently clamped (brute-force nearest-color search)
//   - Outline is implemented via vertex normal extrusion in a single pass using
//     the CULL_FRONT trick (a second draw call with reversed culling). In Godot 4
//     you achieve this by putting the outline logic in vertex() and toggling it
//     via a bool uniform so you can instance the same material twice on a
//     MeshInstance3D with two surface materials.
// =============================================================================

shader_type spatial;

// Standard rendering with custom lighting — NOT unshaded.
// We still want Godot's light loop to run so our light() function fires,
// but we disable specular from the built-in PBR pass (we handle it ourselves)
// and disable ambient occlusion / fog accumulation from environment.
render_mode
    diffuse_lambert,      // use Lambert as the *base* before we quantize it
    specular_disabled,    // we handle rim light manually; no PBR specular glare
    ambient_light_disabled; // we inject our own ambient in fragment() for unity

// =============================================================================
// UNIFORMS
// =============================================================================

// --- Base Color -----------------------------------------------------------
// The albedo source. If use_vertex_color is true, vertex color drives albedo
// (recommended — the Blender pipeline bakes palette colors into vertex data).
// Otherwise the flat base_color uniform is used for the whole mesh.
uniform vec4 base_color : source_color = vec4(1.0);
uniform bool use_vertex_color = true;

// --- UV Albedo Texture ----------------------------------------------------
// When use_albedo_texture is true, diffuse color is sampled from this texture
// using UV coordinates rather than vertex color or the flat base_color.
// The base_color is then applied as a 25 % faction-tint overlay on top so
// faction identity is preserved without crushing dark or saturated textures.
// repeat_enable ensures tiling UVs (values > 1.0) work correctly.
uniform sampler2D albedo_texture : filter_linear_mipmap, repeat_enable;
uniform bool use_albedo_texture = false;

// --- Cel / Band Lighting --------------------------------------------------
// light_bands controls how many discrete brightness steps exist.
//   2 bands → very graphic, two-tone look
//   4 bands → default; readable and stylised
//   8 bands → almost smooth; subtle quantization
// band_smoothness adds a small feather at each band edge to prevent hard
// aliasing on curved surfaces under moving lights.
uniform int light_bands : hint_range(2, 8) = 4;
uniform float band_smoothness : hint_range(0.0, 0.5) = 0.05;

// --- Palette Enforcement --------------------------------------------------
// A 1×N ImageTexture containing the allowed palette colors (max MAX_PALETTE_SIZE).
// When enforce_palette is true, the final albedo is snapped toward the nearest
// palette entry, mixed by palette_strength (0 = no snap, 1 = hard snap).
// Use filter_nearest so we read exact palette colors without interpolation.
uniform sampler2D palette_texture : filter_nearest;
uniform bool enforce_palette = false;
uniform float palette_strength : hint_range(0.0, 1.0) = 0.8;

// --- Ambient Light --------------------------------------------------------
// We inject our own ambient so every asset shares *exactly* the same ambient
// tint, regardless of the Godot environment's WorldEnvironment settings.
// This is the most reliable way to enforce a consistent dark/shadow color
// across imported assets that may have been lit differently by their authors.
uniform float ambient_strength : hint_range(0.0, 1.0) = 0.3;
uniform vec4 ambient_color : source_color = vec4(0.2, 0.2, 0.25, 1.0);

// --- Rim Light ------------------------------------------------------------
// A Fresnel-style rim applied in light() to give units a readable silhouette
// against the terrain. Critical for RTS readability.
//   rim_power  → higher = tighter, more glancing rim; lower = wide halo
//   rim_strength → intensity of the rim contribution
//   rim_color  → usually white or slightly warm to feel like skylight bounce
uniform float rim_power : hint_range(0.0, 8.0) = 3.0;
uniform float rim_strength : hint_range(0.0, 1.0) = 0.15;
uniform vec4 rim_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

// --- Team Color (Faction Tinting) -----------------------------------------
// Mixes the base albedo toward team_color by team_color_strength.
// Keep team_color_strength between 0.2–0.5 for natural-looking tints.
// The Blender pipeline reserves a vertex color channel for team-color masks;
// this uniform controls the global mix regardless of mask data.
uniform vec4 team_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform float team_color_strength : hint_range(0.0, 1.0) = 0.0;

// --- Outline (CULL_FRONT pass) --------------------------------------------
// When outline_width > 0.0, the vertex function extrudes vertices along their
// normals. To use this as a proper outline:
//   1. Assign this material to surface 0 (normal render)
//   2. Duplicate the material, set outline_width > 0, set is_outline_pass = true
//      on the duplicate, and assign it to surface 1 of the same mesh.
// The CULL_FRONT render mode on the outline pass (combined with the extrusion)
// creates a visible shell around the model.
uniform float outline_width : hint_range(0.0, 0.05) = 0.0;
uniform vec4 outline_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
// Set true on the outline-pass material instance only.
// When true, the fragment outputs the flat outline_color and skips all lighting.
uniform bool is_outline_pass = false;

// --- Procedural Surface Noise ---------------------------------------------
// A hash-based 3D noise applied to albedo to break up the digital flatness of
// solid vertex colors. At noise_strength 0.02 it is barely perceptible but
// adds cohesion — all assets share the same micro-texture signature.
// noise_scale controls the world-space frequency (higher = finer grain).
uniform float noise_strength : hint_range(0.0, 0.1) = 0.02;
uniform float noise_scale : hint_range(1.0, 100.0) = 30.0;

// =============================================================================
// VARYINGS
// =============================================================================

// Vertex color passed from vertex() to fragment().
// We use a varying rather than reading COLOR directly in fragment() so we can
// intercept and modify it in vertex() if needed.
varying vec4 v_vertex_color;

// World-space position for noise — avoids UV dependency so every mesh type
// (including ones without UVs) gets coherent noise.
varying vec3 v_world_pos;

// =============================================================================
// HELPER FUNCTIONS
// =============================================================================

// --- hash3 ----------------------------------------------------------------
// A compact, fast 3D hash function that returns a pseudo-random vec3 in [0,1].
// Based on the classic "sin hash" pattern. Not cryptographically random but
// visually sufficient for surface grain noise.
//
// Input:  p — world-space position (already scaled by noise_scale)
// Output: pseudo-random vec3 in range [0, 1]
vec3 hash3(vec3 p) {
    // Fold the input into three overlapping dot products to mix all axes.
    // The large irrational multipliers spread the frequency across the spectrum.
    p = vec3(
        dot(p, vec3(127.1, 311.7, 74.7)),
        dot(p, vec3(269.5, 183.3, 246.1)),
        dot(p, vec3(113.5, 271.9, 124.6))
    );
    // sin() wraps the values into [-1,1]; fract() maps them to [0,1].
    // The large multiplier (43758.5453) ensures a wide, non-repeating period
    // over the typical world scales used in an RTS game.
    return fract(sin(p) * 43758.5453);
}

// --- procedural_noise -----------------------------------------------------
// Returns a smooth noise value in [-0.5, 0.5] suitable for adding to albedo.
// Uses trilinear interpolation between 8 hash samples (value noise).
//
// Input:  p — world-space position (pre-scaled by noise_scale in fragment)
// Output: float noise in approximately [-0.5, 0.5]
float procedural_noise(vec3 p) {
    vec3 i = floor(p);   // integer cell
    vec3 f = fract(p);   // fractional position within cell

    // Smoothstep for C1-continuous interpolation (avoids banding at cell edges)
    vec3 u = f * f * (3.0 - 2.0 * f);

    // Sample all 8 corners of the unit cube and interpolate
    float n000 = hash3(i + vec3(0.0, 0.0, 0.0)).x;
    float n100 = hash3(i + vec3(1.0, 0.0, 0.0)).x;
    float n010 = hash3(i + vec3(0.0, 1.0, 0.0)).x;
    float n110 = hash3(i + vec3(1.0, 1.0, 0.0)).x;
    float n001 = hash3(i + vec3(0.0, 0.0, 1.0)).x;
    float n101 = hash3(i + vec3(1.0, 0.0, 1.0)).x;
    float n011 = hash3(i + vec3(0.0, 1.0, 1.0)).x;
    float n111 = hash3(i + vec3(1.0, 1.0, 1.0)).x;

    // Trilinear interpolation
    float result = mix(
        mix(mix(n000, n100, u.x), mix(n010, n110, u.x), u.y),
        mix(mix(n001, n101, u.x), mix(n011, n111, u.x), u.y),
        u.z
    );

    // Remap [0,1] → [-0.5,0.5] so we can add/subtract from albedo symmetrically
    return result - 0.5;
}

// --- nearest_palette_color ------------------------------------------------
// Brute-force nearest-color lookup against the palette texture.
// The palette texture is a 1×N image; each pixel is one palette entry.
// We iterate up to MAX_PALETTE_SIZE texels and track the closest color in
// linear RGB (squared Euclidean distance — no sqrt needed for comparison).
//
// Inputs:
//   color        — the albedo to snap
//   palette      — the 1×N sampler2D
//   palette_size — number of valid colors (texels) in the texture
// Returns: nearest palette color as vec3
//
// NOTE: palette_size must be a compile-time expression or a loop bound known
// to the GPU driver. We cap it at MAX_PALETTE_SIZE and rely on the caller
// to pass the correct count. Colors beyond palette_size are never chosen
// because their distance is only compared while idx < palette_size.
const int MAX_PALETTE_SIZE = 32; // supports palettes up to 32 colors

vec3 nearest_palette_color(vec3 color, sampler2D palette, int palette_size) {
    vec3  best_color = color;
    float best_dist  = 1e9; // start with an impossibly large distance

    for (int i = 0; i < MAX_PALETTE_SIZE; i++) {
        if (i >= palette_size) break; // skip unused palette slots

        // UV: x is the texel center for column i in a 1×N texture.
        // y = 0.5 puts us in the center of the single row.
        float u = (float(i) + 0.5) / float(MAX_PALETTE_SIZE);
        vec3 palette_entry = texture(palette, vec2(u, 0.5)).rgb;

        // Squared Euclidean distance in RGB space.
        // For perceptual accuracy a luminance-weighted metric would be better,
        // but squared RGB is sufficient for palette sizes ≤ 32 and avoids
        // extra instructions on the GPU.
        vec3  delta = color - palette_entry;
        float dist  = dot(delta, delta);

        if (dist < best_dist) {
            best_dist  = dist;
            best_color = palette_entry;
        }
    }

    return best_color;
}

// =============================================================================
// VERTEX FUNCTION
// =============================================================================

void vertex() {
    // Pass vertex color to fragment via varying.
    // COLOR is available in vertex() in Godot 4 and holds per-vertex color data
    // imported from the mesh (e.g., baked by the Blender pipeline).
    v_vertex_color = COLOR;

    // Store world-space position for noise calculation in fragment().
    // MODEL_MATRIX transforms from local space to world space.
    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    // --- Outline vertex extrusion (CULL_FRONT pass only) ------------------
    // When this material instance is the outline pass, we push each vertex
    // outward along its normal by outline_width units in *view space*.
    // Using view-space normals means the outline width is consistent regardless
    // of camera distance — important for RTS cameras that zoom in and out.
    //
    // The paired surface (surface 0) uses is_outline_pass = false and normal
    // culling, so the extruded geometry is only visible around the silhouette.
    if (is_outline_pass && outline_width > 0.0) {
        // NORMAL is in local space; transform to view space for consistent width.
        // We ignore the w component because normals are direction vectors.
        vec3 view_normal = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);

        // Extrude along the view-space normal, then back to clip space.
        // POSITION is already in clip space in Godot 4's vertex function.
        // We offset in clip-space XY proportional to the W component so
        // the extrusion stays screen-space consistent (no perspective shrink).
        POSITION = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
        POSITION.xy += view_normal.xy * outline_width * POSITION.w;
    }
}

// =============================================================================
// FRAGMENT FUNCTION
// =============================================================================

void fragment() {
    // --- Outline pass early-out -------------------------------------------
    // If this is the outline pass, output the flat outline color immediately
    // and skip all lighting / noise / palette work. The light() function will
    // still be called by Godot's pipeline but DIFFUSE_LIGHT will be zeroed
    // out implicitly because ALBEDO drives the lit result and we return here
    // with a flat color. We set ROUGHNESS 1.0 to suppress any specular glint.
    if (is_outline_pass) {
        ALBEDO    = outline_color.rgb;
        ALPHA     = outline_color.a;
        ROUGHNESS = 1.0;
        METALLIC  = 0.0;
        return;
    }

    // --- Step 1: Determine base albedo ------------------------------------
    // Three sources in priority order:
    //   1. UV albedo texture (use_albedo_texture = true): sampled from UV coords.
    //      base_color is mixed in as a 25 % faction-tint overlay so faction
    //      identity reads without crushing the original texture colours.
    //   2. Vertex color (use_vertex_color = true): baked by the Blender pipeline.
    //      base_color modulates by multiplication — vertex colour encodes surface
    //      variation, base_color encodes the faction secondary tint.
    //   3. Flat base_color only: used for procedural / fallback geometry.
    vec3 albedo;
    if (use_albedo_texture) {
        vec3 tex_sample = texture(albedo_texture, UV).rgb;
        // 25 % overlay: preserves texture detail while adding faction tint.
        albedo = mix(tex_sample, base_color.rgb, 0.25);
    } else if (use_vertex_color) {
        albedo = v_vertex_color.rgb * base_color.rgb;
    } else {
        albedo = base_color.rgb;
    }

    // --- Step 2: Team color tint ------------------------------------------
    // Linearly interpolate from the base albedo toward the team color.
    // At team_color_strength = 0.0 this is a no-op.
    // Typical faction coloring uses 0.2–0.4 so the original hue still reads.
    albedo = mix(albedo, team_color.rgb, team_color_strength);

    // --- Step 3: Procedural surface noise ---------------------------------
    // Hash-based value noise in world space. Scales the position by noise_scale
    // so the grain frequency is independent of scene scale.
    //
    // We add a small signed noise offset to each RGB channel independently
    // by sampling noise at three slightly offset positions. This produces
    // a subtle chromatic micro-variation rather than a uniform brightness shift,
    // which looks more organic and ties all assets together visually.
    vec3 noise_pos = v_world_pos * noise_scale;
    float noise_r = procedural_noise(noise_pos);
    float noise_g = procedural_noise(noise_pos + vec3(31.41, 0.0,  0.0));
    float noise_b = procedural_noise(noise_pos + vec3(0.0,  27.18, 0.0));
    vec3 noise_rgb = vec3(noise_r, noise_g, noise_b);

    // Add noise, then clamp to [0,1] to prevent HDR blowout on bright colors.
    albedo = clamp(albedo + noise_rgb * noise_strength, 0.0, 1.0);

    // --- Step 4: Palette enforcement (optional) ---------------------------
    // Find the nearest palette color and mix toward it.
    // palette_size is inferred from the texture dimensions at runtime.
    // We read textureSize() to get the actual height (N in a 1×N texture).
    if (enforce_palette) {
        int palette_size = textureSize(palette_texture, 0).y;
        // Cap at our compile-time maximum to satisfy the loop bound
        palette_size = min(palette_size, MAX_PALETTE_SIZE);

        vec3 snapped = nearest_palette_color(albedo, palette_texture, palette_size);
        albedo = mix(albedo, snapped, palette_strength);
    }

    // --- Step 5: Write outputs --------------------------------------------
    // ALBEDO is the base color handed to the light() function.
    // We handle ambient here by adding it directly to EMISSION so it is
    // completely decoupled from Godot's environment/WorldEnvironment settings —
    // every asset in the game shares exactly the same shadow color.
    ALBEDO    = albedo;
    EMISSION  = ambient_color.rgb * ambient_strength * albedo;
    ROUGHNESS = 1.0;  // matte look; no PBR highlight
    METALLIC  = 0.0;
    // Alpha matches the active albedo source so each path respects transparency.
    if (use_albedo_texture) {
        ALPHA = texture(albedo_texture, UV).a * base_color.a;
    } else if (use_vertex_color) {
        ALPHA = v_vertex_color.a * base_color.a;
    } else {
        ALPHA = base_color.a;
    }
}

// =============================================================================
// LIGHT FUNCTION
// =============================================================================
//
// Called once per active light source in the scene. We implement:
//   1. Quantized (banded) diffuse — cel shading
//   2. Rim light — silhouette readability for RTS units
//
// Godot 4 light() parameters relevant here:
//   NORMAL    — surface normal in view space
//   LIGHT     — direction to the light in view space
//   VIEW      — direction to the camera in view space
//   LIGHT_COLOR — the light's color × energy
//   ATTENUATION — shadow/distance falloff [0,1]
//   ALBEDO    — the albedo written in fragment()
//   DIFFUSE_LIGHT — OUTPUT: accumulated diffuse contribution (additive)
//   SPECULAR_LIGHT — OUTPUT: accumulated specular contribution (we leave at 0)

void light() {
    // --- Step 1: Raw diffuse (Lambert) ------------------------------------
    // NdotL in view space. Clamp to [0,1] so back-facing surfaces stay dark.
    float NdotL = max(dot(NORMAL, LIGHT), 0.0);

    // --- Step 2: Quantize to bands (cel shading) --------------------------
    // We map NdotL into discrete bands:
    //
    //   quantized = floor(NdotL * bands + smoothness) / bands
    //
    // The + band_smoothness term shifts the threshold slightly so the
    // transition between bands happens just before the mathematical midpoint,
    // adding a subtle anti-aliasing effect without a true smoothstep.
    // At band_smoothness = 0.0 the bands are perfectly hard-edged.
    //
    // Examples with light_bands = 4:
    //   NdotL 0.00–0.24 → band 0 (shadow)
    //   NdotL 0.25–0.49 → band 1
    //   NdotL 0.50–0.74 → band 2
    //   NdotL 0.75–1.00 → band 3 (highlight)
    float bands     = float(light_bands);
    float quantized = floor(NdotL * bands + band_smoothness) / bands;

    // Apply ATTENUATION so shadows from shadow-casting lights are respected.
    quantized *= ATTENUATION;

    // Accumulate diffuse. DIFFUSE_LIGHT is additive across all light calls.
    // Multiply by ALBEDO so the base color is preserved under the cel shading.
    DIFFUSE_LIGHT += quantized * LIGHT_COLOR * ALBEDO;

    // --- Step 3: Rim light ------------------------------------------------
    // Fresnel-style rim: bright where the surface faces away from the camera.
    // pow(1 - NdotV, rim_power) peaks at glancing angles.
    //
    //   rim_power = 1.0 → very wide, soft halo
    //   rim_power = 3.0 → medium rim (default; good for units)
    //   rim_power = 6.0 → tight edge highlight only
    //
    // We only add rim where the surface is *also* lit (NdotL > 0) to avoid
    // a halo on the shadow side, which looks wrong for RTS-style units.
    float NdotV = max(dot(NORMAL, VIEW), 0.0);
    float rim   = pow(1.0 - NdotV, rim_power) * rim_strength;

    // Modulate rim by NdotL so it only appears on the light-facing hemisphere.
    // This prevents the rim from "wrapping around" into the shadow, which
    // tends to look like a Fresnel material rather than a rim light.
    rim *= clamp(NdotL * 2.0, 0.0, 1.0);

    // Add rim to diffuse (using rim_color allows colored rims, e.g. warm sky).
    DIFFUSE_LIGHT += rim * rim_color.rgb * LIGHT_COLOR;

    // SPECULAR_LIGHT intentionally left at 0 — we use specular_disabled render
    // mode and handle all highlight-style effects via rim above.
}
