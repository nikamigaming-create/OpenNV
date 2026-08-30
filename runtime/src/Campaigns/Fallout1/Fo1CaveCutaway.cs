using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

// The roof remains a source-topology cut. Camera-facing cave shells use an
// opaque, screen-dithered PBR cutaway so discarded fragments neither blend in
// the transparent queue nor write depth over the player and Vault entrance.
// The MAP walk mask and all source placement remain untouched.
internal partial class Fo1CaveCutaway : Node
{
    private const float InteriorCoverage = 0.0f;
    private const float EdgeFraction = 0.14f;
    private const float FocusRadiusScale = 3.10f;
    private const float VaultRadiusScale = 2.55f;

    private const string CutawayShader = """
        shader_type spatial;
        render_mode cull_disabled, depth_draw_opaque, shadows_disabled;

        uniform sampler2D albedo_texture : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D normal_texture : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform vec4 albedo_tint : source_color = vec4(1.0);
        uniform float world_scale = 0.5;
        uniform float triplanar_sharpness = 4.0;
        uniform float roughness_value = 0.9;
        uniform float normal_strength = 1.0;
        uniform vec2 focus_center_uv = vec2(0.5);
        uniform vec2 focus_radius_uv = vec2(0.15, 0.22);
        uniform vec2 vault_center_uv = vec2(0.5);
        uniform vec2 vault_radius_uv = vec2(0.12, 0.18);
        uniform float interior_coverage = 0.0;
        uniform float edge_fraction = 0.14;

        varying vec3 world_position;

        vec3 projection_weights(vec3 geometric_normal) {
            vec3 weights = pow(abs(geometric_normal), vec3(triplanar_sharpness));
            return weights / max(weights.x + weights.y + weights.z, 0.0001);
        }

        vec3 triplanar_albedo(vec3 point, vec3 geometric_normal) {
            vec3 weights = projection_weights(geometric_normal);
            vec3 first = texture(albedo_texture, point.yz).rgb;
            vec3 second = texture(albedo_texture, point.xz).rgb;
            vec3 third = texture(albedo_texture, point.xy).rgb;
            return first * weights.x + second * weights.y + third * weights.z;
        }

        vec3 sampled_normal(vec2 coordinates) {
            vec3 sampled = texture(normal_texture, coordinates).rgb * 2.0 - 1.0;
            return normalize(vec3(sampled.xy * normal_strength, sampled.z));
        }

        vec3 triplanar_normal(vec3 point, vec3 geometric_normal) {
            vec3 weights = projection_weights(geometric_normal);
            vec3 orientation = sign(geometric_normal);
            vec3 first_sample = sampled_normal(point.yz);
            vec3 second_sample = sampled_normal(point.xz);
            vec3 third_sample = sampled_normal(point.xy);
            vec3 first = vec3(first_sample.z * orientation.x, first_sample.x, first_sample.y);
            vec3 second = vec3(second_sample.x, second_sample.z * orientation.y, second_sample.y);
            vec3 third = vec3(third_sample.x, third_sample.y, third_sample.z * orientation.z);
            return normalize(first * weights.x + second * weights.y + third * weights.z);
        }

        float ellipse_distance(vec2 uv, vec2 center, vec2 radius) {
            return length((uv - center) / max(radius, vec2(0.0001)));
        }

        float screen_dither(vec2 pixel) {
            // Stable interleaved-gradient noise: no transparent sorting and no
            // world-space moire when the tactical camera moves.
            return fract(52.9829189 * fract(dot(floor(pixel), vec2(0.06711056, 0.00583715))));
        }

        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            float focus_distance = ellipse_distance(SCREEN_UV, focus_center_uv, focus_radius_uv);
            float vault_distance = ellipse_distance(SCREEN_UV, vault_center_uv, vault_radius_uv);
            float reveal_distance = min(focus_distance, vault_distance);
            float edge = smoothstep(1.0 - edge_fraction, 1.0 + edge_fraction, reveal_distance);
            float coverage = mix(interior_coverage, 1.0, edge);
            if (screen_dither(FRAGCOORD.xy) > coverage) {
                discard;
            }

            vec3 world_normal = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
            vec3 sample_point = world_position * world_scale;
            ALBEDO = triplanar_albedo(sample_point, world_normal) * albedo_tint.rgb;
            ROUGHNESS = roughness_value;
            METALLIC = 0.0;
            vec3 mapped_world_normal = triplanar_normal(sample_point, world_normal);
            NORMAL = normalize((VIEW_MATRIX * vec4(mapped_world_normal, 0.0)).xyz);
        }
        """;

    private Node3D[] _sourceTacticalHidden = [];
    private Occluder[] _tacticalOccluders = [];
    private ShaderMaterial[] _cutawayMaterials = [];
    private Fo1TacticalSession? _session;
    private Camera3D? _camera;
    private Fo1CutawayProfile _profile = null!;
    private bool _cutawayEnabled = true;
    private int _fadedInstances;

    internal int Candidates => _sourceTacticalHidden.Length;
    internal int HiddenInstances => _sourceTacticalHidden.Count(surface => !surface.Visible);
    internal int FadedInstances => _fadedInstances;
    internal int MeltMaterials => _cutawayMaterials.Length;
    internal bool ShaderDriven => _cutawayMaterials.Length > 0;
    internal bool SourceVisibilityDriven => _sourceTacticalHidden.Length > 0;

    internal void Configure(
        Node3D container,
        Fo1TacticalSession session,
        Camera3D camera,
        Fo1CutawayProfile profile)
    {
        Name = "Fo1SourceRoofAndDitheredShellCutaway";
        _session = session;
        _camera = camera;
        _profile = profile;
        _sourceTacticalHidden = Descendants<Node3D>(container)
            .Where(surface => SourceVisibility(surface) == "hide-roof-envelope")
            .ToArray();
        if (_sourceTacticalHidden.Length != 1)
            throw new InvalidOperationException(
                $"Fallout owned cave source roof coverage drifted: {_sourceTacticalHidden.Length}");

        // Only rock matter participates. Vault hardware is a destination landmark
        // and remains opaque behind the source-shaped rock portal.
        _tacticalOccluders = Descendants<Node3D>(container)
            .Where(surface => SourceVisibility(surface) is
                "hide-boundary-envelope" or "hide-wall-volume" or "hide-vault-portal")
            .Select(BuildOccluder)
            .ToArray();
        if (_tacticalOccluders.Length < 3)
            throw new InvalidOperationException(
                "Fallout cave tactical shell cutaway coverage is incomplete.");
        _cutawayMaterials = _tacticalOccluders
            .SelectMany(occluder => occluder.Geometry)
            .SelectMany(row => row.CutawayMaterials)
            .DistinctBy(material => material.GetInstanceId())
            .ToArray();
        ApplyCutaway();
    }

    internal void SetMeltEnabled(bool enabled)
    {
        _cutawayEnabled = enabled;
        ApplyCutaway();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        ApplyCutaway();
    }

    private void ApplyCutaway()
    {
        if (_camera is null || _session is null)
            return;
        foreach (var surface in _sourceTacticalHidden)
            surface.Visible = !_cutawayEnabled;

        if (!_cutawayEnabled)
        {
            _fadedInstances = 0;
            foreach (var occluder in _tacticalOccluders)
                occluder.Restore();
            return;
        }

        UpdateShaderFocus();
        _fadedInstances = 0;
        foreach (var occluder in _tacticalOccluders)
        {
            occluder.ApplyCutaway();
            _fadedInstances += occluder.Geometry.Length;
        }
    }

    private void UpdateShaderFocus()
    {
        var viewportSize = _camera!.GetViewport().GetVisibleRect().Size;
        if (viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
            return;
        var player = _session!.PlayerToken.GlobalPosition +
            Vector3.Up * _profile.PlayerFocusHeightMeters;
        var points = _session.SelectedMob is { } selected
            ? new[]
            {
                _camera.UnprojectPosition(player),
                _camera.UnprojectPosition(
                    selected.GlobalPosition + Vector3.Up * _profile.TargetFocusHeightMeters),
            }
            : new[] { _camera.UnprojectPosition(player) };
        var minimum = points.Aggregate((one, two) => one.Min(two));
        var maximum = points.Aggregate((one, two) => one.Max(two));
        var horizontalTunnel = MathF.Max(
            _profile.ScreenMarginPixels * FocusRadiusScale,
            viewportSize.X * 0.40f);
        minimum.X -= horizontalTunnel;
        maximum.X += horizontalTunnel;
        minimum.Y -= MathF.Max(
            _profile.ScreenMarginPixels * FocusRadiusScale,
            viewportSize.Y * 0.16f);
        maximum.Y = MathF.Max(maximum.Y, viewportSize.Y * 1.05f);
        var focusCenterPixels = (minimum + maximum) * 0.5f;
        var focusRadiusPixels = (maximum - minimum) * 0.5f;
        var vaultCenterPixels = _camera.UnprojectPosition(
            Fo1HexMath.Center(_session.DoorTile) + Vector3.Up * 1.65f);
        var vaultRadiusPixels = new Vector2(
            MathF.Max(
                _profile.ScreenMarginPixels * VaultRadiusScale,
                viewportSize.X * 0.26f),
            MathF.Max(
                _profile.ScreenMarginPixels * VaultRadiusScale * 1.15f,
                viewportSize.Y * 0.28f));
        foreach (var material in _cutawayMaterials)
        {
            material.SetShaderParameter("focus_center_uv", focusCenterPixels / viewportSize);
            material.SetShaderParameter("focus_radius_uv", focusRadiusPixels / viewportSize);
            material.SetShaderParameter("vault_center_uv", vaultCenterPixels / viewportSize);
            material.SetShaderParameter("vault_radius_uv", vaultRadiusPixels / viewportSize);
        }
    }

    private static Occluder BuildOccluder(Node3D root)
    {
        var instances = root is GeometryInstance3D rootGeometry
            ? Descendants<GeometryInstance3D>(root).Prepend(rootGeometry)
            : Descendants<GeometryInstance3D>(root);
        var geometry = instances
            .Where(instance => instance.Visible)
            .Select(BuildCutawayGeometry)
            .Where(row => row is not null)
            .Select(row => row!)
            .ToArray();
        if (geometry.Length == 0)
            throw new InvalidOperationException(
                $"Fallout cave cutaway has no visible geometry: {root.Name}");
        return new Occluder(root, WorldBounds(root), geometry);
    }

    private static CutawayGeometry? BuildCutawayGeometry(GeometryInstance3D instance)
    {
        if (instance.MaterialOverride is StandardMaterial3D overrideMaterial)
            return new CutawayGeometry(
                instance,
                overrideMaterial,
                CreateCutawayMaterial(overrideMaterial),
                [],
                instance.CastShadow);
        if (instance is not MeshInstance3D mesh || mesh.Mesh is null)
            return null;
        var surfaces = new List<CutawaySurface>();
        var materials = new Dictionary<ulong, ShaderMaterial>();
        for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D standard ||
                standard.ResourceName.StartsWith(
                    "FO1 hidden owned cave-wall surface",
                    StringComparison.Ordinal))
                continue;
            if (!materials.TryGetValue(standard.GetInstanceId(), out var cutaway))
            {
                cutaway = CreateCutawayMaterial(standard);
                materials.Add(standard.GetInstanceId(), cutaway);
            }
            surfaces.Add(new CutawaySurface(
                surface,
                mesh.GetSurfaceOverrideMaterial(surface),
                cutaway));
        }
        return surfaces.Count == 0
            ? null
            : new CutawayGeometry(
                instance,
                null,
                null,
                surfaces.ToArray(),
                instance.CastShadow);
    }

    private static ShaderMaterial CreateCutawayMaterial(StandardMaterial3D standard)
    {
        if (standard.AlbedoTexture is null || standard.NormalTexture is null)
            throw new InvalidOperationException(
                $"Fallout cave cutaway requires the unified PBR rock textures: {standard.ResourceName}");
        var cutaway = new ShaderMaterial
        {
            ResourceName = $"{standard.ResourceName} tactical dither cutaway",
            Shader = new Shader { Code = CutawayShader },
        };
        cutaway.SetShaderParameter("albedo_texture", standard.AlbedoTexture);
        cutaway.SetShaderParameter("normal_texture", standard.NormalTexture);
        cutaway.SetShaderParameter("albedo_tint", standard.AlbedoColor);
        cutaway.SetShaderParameter("world_scale", standard.Uv1Scale.X);
        cutaway.SetShaderParameter("triplanar_sharpness", standard.Uv1TriplanarSharpness);
        cutaway.SetShaderParameter("roughness_value", standard.Roughness);
        cutaway.SetShaderParameter("normal_strength", standard.NormalScale);
        cutaway.SetShaderParameter("interior_coverage", InteriorCoverage);
        cutaway.SetShaderParameter("edge_fraction", EdgeFraction);
        return cutaway;
    }

    private static string SourceVisibility(Node3D surface) =>
        surface.HasMeta("fo1_source_tactical_visibility")
            ? surface.GetMeta("fo1_source_tactical_visibility").AsString()
            : string.Empty;

    private static Aabb WorldBounds(Node3D root)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var meshes = root is MeshInstance3D rootMesh
            ? Descendants<MeshInstance3D>(root).Prepend(rootMesh)
            : Descendants<MeshInstance3D>(root);
        var count = 0;
        foreach (var mesh in meshes)
        {
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = mesh.ToGlobal(new Vector3(x, y, z));
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                    }
            count++;
        }
        if (count == 0)
            throw new InvalidOperationException(
                $"Fallout cave occluder has no meshes: {root.Name}");
        return new Aabb(minimum, maximum - minimum);
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed record CutawayGeometry(
        GeometryInstance3D Instance,
        Material? OriginalOverride,
        ShaderMaterial? CutawayOverride,
        CutawaySurface[] Surfaces,
        GeometryInstance3D.ShadowCastingSetting OriginalShadow)
    {
        internal IEnumerable<ShaderMaterial> CutawayMaterials =>
            CutawayOverride is null
                ? Surfaces.Select(surface => surface.CutawayMaterial)
                : Surfaces.Select(surface => surface.CutawayMaterial).Prepend(CutawayOverride);

        internal void ApplyCutaway()
        {
            if (CutawayOverride is not null)
                Instance.MaterialOverride = CutawayOverride;
            if (Instance is MeshInstance3D mesh)
                foreach (var surface in Surfaces)
                    mesh.SetSurfaceOverrideMaterial(surface.Index, surface.CutawayMaterial);
            Instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        internal void Restore()
        {
            Instance.MaterialOverride = OriginalOverride;
            if (Instance is MeshInstance3D mesh)
                foreach (var surface in Surfaces)
                    mesh.SetSurfaceOverrideMaterial(surface.Index, surface.OriginalOverride);
            Instance.CastShadow = OriginalShadow;
        }
    }

    private sealed record CutawaySurface(
        int Index,
        Material? OriginalOverride,
        ShaderMaterial CutawayMaterial);

    private sealed record Occluder(Node3D Root, Aabb Bounds, CutawayGeometry[] Geometry)
    {
        internal void ApplyCutaway()
        {
            Root.Visible = true;
            foreach (var row in Geometry)
                row.ApplyCutaway();
        }

        internal void Restore()
        {
            Root.Visible = true;
            foreach (var row in Geometry)
                row.Restore();
        }
    }
}
