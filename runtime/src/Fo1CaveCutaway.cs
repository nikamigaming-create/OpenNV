using Godot;

namespace OpenNV.Runtime;

internal static class Fo1CaveCutawayNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float GeometryFloat0Point5f = 0.5f;
    internal const float GeometryFloat0Point99f = 0.99f;
}

internal partial class Fo1CaveCutaway : Node
{
    private Entry[] _entries = [];
    private ShaderMaterial[] _materials = [];
    private Fo1TacticalSession? _session;
    private Camera3D? _camera;
    private Fo1CutawayProfile _profile = null!;
    private bool _meltEnabled = true;

    internal int Candidates => _entries.Length;
    internal int HiddenInstances { get; private set; }
    internal int MeltMaterials => _materials.Length;
    internal bool ShaderDriven => _materials.Length > 0;

    internal void Configure(
        Node3D container,
        Fo1TacticalSession session,
        Camera3D camera,
        Fo1CutawayProfile profile)
    {
        Name = "Fo1CaveCameraMelt";
        _session = session;
        _camera = camera;
        _profile = profile;
        _entries = container.GetChildren()
            .OfType<Node3D>()
            .Where(node =>
                !node.HasMeta("fo1_cutaway_exempt") || !node.GetMeta("fo1_cutaway_exempt").AsBool())
            .Where(node => node is not Light3D)
            .Where(node => IsMeltCandidate(node, profile))
            .Select(node => new Entry(node, WorldBounds(node)))
            .ToArray();
        if (_entries.Length < profile.MinimumCandidateInstances)
            throw new InvalidOperationException(
                $"Fallout cave melt has incomplete geometry coverage: {_entries.Length}");

        var shader = BuildMeltShader();
        var white = SolidTexture(Colors.White);
        var flatNormal = SolidTexture(new Color(Fo1CaveCutawayNumericContracts.GeometryFloat0Point5f, Fo1CaveCutawayNumericContracts.GeometryFloat0Point5f, 1.0f, 1.0f));
        _materials = _entries
            .SelectMany(entry => PrepareMeltMaterials(entry, shader, white, flatNormal, profile))
            .ToArray();
        if (_materials.Length < _entries.Length)
            throw new InvalidOperationException(
                $"Fallout cave melt material coverage is incomplete: " +
                $"materials={_materials.Length} entries={_entries.Length}");
        SetMeltEnabled(true);
    }

    internal void SetMeltEnabled(bool enabled)
    {
        _meltEnabled = enabled;
        foreach (var material in _materials)
            material.SetShaderParameter("melt_enabled", enabled);
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_session is null || _camera is null)
            return;

        var player = _session.PlayerToken.GlobalPosition +
            Vector3.Up * _profile.PlayerFocusHeightMeters;
        var focus = _session.SelectedMob is { } selected
            ? selected.GlobalPosition + Vector3.Up * _profile.TargetFocusHeightMeters
            : player;
        var tactical = _camera.Projection == Camera3D.ProjectionType.Orthogonal;
        foreach (var material in _materials)
        {
            material.SetShaderParameter("melt_camera", _camera.GlobalPosition);
            material.SetShaderParameter("melt_target_a", player);
            material.SetShaderParameter("melt_target_b", focus);
            material.SetShaderParameter("melt_enabled", _meltEnabled);
            material.SetShaderParameter("melt_tactical", tactical);
        }

        var targets = focus.IsEqualApprox(player)
            ? new[] { player }
            : new[] { player, focus };
        HiddenInstances = _entries.Count(entry => targets.Any(target =>
            CrowdsTarget(entry.Bounds, target) || Occludes(entry.Bounds, target, _camera)));
    }

    private static IEnumerable<ShaderMaterial> PrepareMeltMaterials(
        Entry entry,
        Shader shader,
        Texture2D white,
        Texture2D flatNormal,
        Fo1CutawayProfile profile)
    {
        var role = Role(entry.Root);
        var radius = profile.MeltRadius(role);
        var tacticalCutHeight = profile.TacticalCutHeight(role);
        foreach (var mesh in Meshes(entry.Root))
        {
            var count = mesh.Mesh?.GetSurfaceCount() ?? 0;
            var activeMaterials = Enumerable.Range(0, count)
                .Select(mesh.GetActiveMaterial)
                .ToArray();
            mesh.MaterialOverride = null;
            for (var surface = 0; surface < count; surface++)
            {
                if (activeMaterials[surface] is ShaderMaterial retailSource &&
                    retailSource.ResourceName.Equals(
                        RuntimeMaterialLoader.RetailAmbientDirectionalLambertResourceName,
                        StringComparison.Ordinal))
                {
                    var retailMelt = PrepareRetailMeltMaterial(
                        retailSource,
                        entry.Root.Name,
                        surface,
                        radius,
                        tacticalCutHeight,
                        profile);
                    mesh.SetSurfaceOverrideMaterial(surface, retailMelt);
                    yield return retailMelt;
                    continue;
                }
                if (activeMaterials[surface] is not StandardMaterial3D source ||
                    source.Transparency is not (
                        BaseMaterial3D.TransparencyEnum.Disabled or
                        BaseMaterial3D.TransparencyEnum.AlphaScissor) ||
                    source.AlbedoColor.A < Fo1CaveCutawayNumericContracts.GeometryFloat0Point99f)
                    continue;

                var material = new ShaderMaterial
                {
                    ResourceName = $"FO1 cave camera melt {entry.Root.Name} {surface}",
                    Shader = shader,
                    RenderPriority = source.RenderPriority,
                };
                material.SetShaderParameter(
                    "albedo_texture",
                    source.AlbedoTexture as Texture2D ?? white);
                material.SetShaderParameter(
                    "normal_texture",
                    source.NormalTexture as Texture2D ?? flatNormal);
                material.SetShaderParameter(
                    "emission_texture",
                    source.EmissionTexture as Texture2D ?? white);
                material.SetShaderParameter("use_albedo_texture", source.AlbedoTexture is not null);
                material.SetShaderParameter(
                    "use_normal_texture",
                    source.NormalEnabled && source.NormalTexture is not null);
                material.SetShaderParameter(
                    "use_emission_texture",
                    source.EmissionEnabled && source.EmissionTexture is not null);
                material.SetShaderParameter("use_triplanar", source.Uv1Triplanar);
                material.SetShaderParameter("triplanar_scale", source.Uv1Scale);
                material.SetShaderParameter("triplanar_offset", source.Uv1Offset);
                material.SetShaderParameter(
                    "triplanar_sharpness",
                    source.Uv1TriplanarSharpness);
                material.SetShaderParameter("albedo_color", source.AlbedoColor);
                material.SetShaderParameter(
                    "alpha_scissor_threshold",
                    source.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor
                        ? source.AlphaScissorThreshold
                        : 0.0f);
                material.SetShaderParameter("normal_scale", source.NormalScale);
                material.SetShaderParameter("roughness_value", source.Roughness);
                material.SetShaderParameter("metallic_value", source.Metallic);
                material.SetShaderParameter(
                    "emission_color",
                    source.EmissionEnabled ? source.Emission : Colors.Black);
                material.SetShaderParameter(
                    "emission_energy",
                    source.EmissionEnabled ? source.EmissionEnergyMultiplier : 0.0f);
                material.SetShaderParameter("melt_radius", radius);
                material.SetShaderParameter("melt_edge", profile.MeltEdgeMeters);
                material.SetShaderParameter("tactical_cut_height", tacticalCutHeight);
                material.SetShaderParameter("melt_enabled", true);
                material.SetShaderParameter("melt_tactical", true);
                mesh.SetSurfaceOverrideMaterial(surface, material);
                yield return material;
            }
        }
    }

    private static ShaderMaterial PrepareRetailMeltMaterial(
        ShaderMaterial source,
        string instanceName,
        int surface,
        float radius,
        float tacticalCutHeight,
        Fo1CutawayProfile profile)
    {
        const string vertexMarker = "void vertex() {";
        const string fragmentMarker = "void fragment() {";
        var sourceShader = source.Shader ?? throw new InvalidOperationException(
            "Fallout retail cave material has no shader.");
        var code = sourceShader.Code;
        if (code.IndexOf(vertexMarker, StringComparison.Ordinal) < 0 ||
            code.IndexOf(vertexMarker, StringComparison.Ordinal) !=
                code.LastIndexOf(vertexMarker, StringComparison.Ordinal) ||
            code.IndexOf(fragmentMarker, StringComparison.Ordinal) < 0 ||
            code.IndexOf(fragmentMarker, StringComparison.Ordinal) !=
                code.LastIndexOf(fragmentMarker, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Fallout retail cave shader lacks unique vertex/fragment injection points.");

        code = code.Replace(
            vertexMarker,
            RetailMeltDeclarations + "\n" + vertexMarker +
                "\n    opennv_melt_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;",
            StringComparison.Ordinal);
        code = code.Replace(
            fragmentMarker,
            fragmentMarker + "\n" + RetailMeltFragment,
            StringComparison.Ordinal);

        var material = source.Duplicate(true) as ShaderMaterial
            ?? throw new InvalidOperationException(
                "Could not duplicate a Fallout retail cave material.");
        var meltShader = sourceShader.Duplicate(true) as Shader
            ?? throw new InvalidOperationException(
                "Could not duplicate a Fallout retail cave shader.");
        meltShader.Code = code;
        material.Shader = meltShader;
        material.ResourceName = $"FO1 retail cave camera melt {instanceName} {surface}";
        material.SetShaderParameter("melt_radius", radius);
        material.SetShaderParameter("melt_edge", profile.MeltEdgeMeters);
        material.SetShaderParameter("tactical_cut_height", tacticalCutHeight);
        material.SetShaderParameter("melt_enabled", true);
        material.SetShaderParameter("melt_tactical", true);
        return material;
    }

    private const string RetailMeltDeclarations = """
        uniform bool melt_enabled = true;
        uniform bool melt_tactical = true;
        uniform vec3 melt_camera;
        uniform vec3 melt_target_a;
        uniform vec3 melt_target_b;
        uniform float melt_radius = 2.0;
        uniform float melt_edge = 0.07;
        uniform float tactical_cut_height = -100.0;
        varying vec3 opennv_melt_world_position;

        float opennv_ordered_dither(vec2 pixel) {
            vec2 cell = mod(floor(pixel), 4.0);
            float row0 = cell.x < 1.0 ? 0.0 : cell.x < 2.0 ? 8.0 : cell.x < 3.0 ? 2.0 : 10.0;
            float row1 = cell.x < 1.0 ? 12.0 : cell.x < 2.0 ? 4.0 : cell.x < 3.0 ? 14.0 : 6.0;
            float row2 = cell.x < 1.0 ? 3.0 : cell.x < 2.0 ? 11.0 : cell.x < 3.0 ? 1.0 : 9.0;
            float row3 = cell.x < 1.0 ? 15.0 : cell.x < 2.0 ? 7.0 : cell.x < 3.0 ? 13.0 : 5.0;
            float value = cell.y < 1.0 ? row0 : cell.y < 2.0 ? row1 : cell.y < 3.0 ? row2 : row3;
            return (value + 0.5) / 16.0;
        }

        float opennv_segment_distance(vec3 point, vec3 start, vec3 finish) {
            vec3 segment = finish - start;
            float position = clamp(
                dot(point - start, segment) / max(dot(segment, segment), 0.0001),
                0.0,
                1.0);
            return length(point - (start + segment * position));
        }

        float opennv_target_melt(vec3 point, vec3 target) {
            float tunnel = opennv_segment_distance(point, melt_camera, target);
            float bubble = length(point - target);
            float distance_to_opening = min(tunnel, bubble);
            return 1.0 - smoothstep(
                max(0.05, melt_radius - melt_edge),
                melt_radius + melt_edge,
                distance_to_opening);
        }
        """;

    private const string RetailMeltFragment = """
            if (melt_enabled) {
                float melt = max(
                    opennv_target_melt(opennv_melt_world_position, melt_target_a),
                    opennv_target_melt(opennv_melt_world_position, melt_target_b));
                if (melt_tactical && tactical_cut_height > -99.0) {
                    float roof_slice = smoothstep(
                        tactical_cut_height - 0.06,
                        tactical_cut_height + 0.06,
                        opennv_melt_world_position.y);
                    melt = max(melt, roof_slice);
                }
                if (melt > opennv_ordered_dither(FRAGCOORD.xy)) {
                    discard;
                }
            }
        """;

    private static string Role(Node3D root) => root.HasMeta("fo1_asset_role")
        ? root.GetMeta("fo1_asset_role").AsString()
        : string.Empty;

    private static bool IsMeltCandidate(Node3D root, Fo1CutawayProfile profile)
    {
        var role = Role(root);
        return role.Length > 0 &&
            (profile.MeltRadiusByRoleMeters.ContainsKey(role) ||
                profile.TacticalCutHeightByRoleMeters.ContainsKey(role));
    }

    private static Shader BuildMeltShader() => new()
    {
        Code = """
            shader_type spatial;
            render_mode cull_disabled, depth_draw_opaque;

            uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
            uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
            uniform sampler2D emission_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
            uniform bool use_albedo_texture = false;
            uniform bool use_normal_texture = false;
            uniform bool use_emission_texture = false;
            uniform bool use_triplanar = false;
            uniform vec3 triplanar_scale = vec3(1.0);
            uniform vec3 triplanar_offset = vec3(0.0);
            uniform float triplanar_sharpness = 1.0;
            uniform vec4 albedo_color : source_color = vec4(1.0);
            uniform float alpha_scissor_threshold = 0.0;
            uniform float normal_scale = 1.0;
            uniform float roughness_value = 1.0;
            uniform float metallic_value = 0.0;
            uniform vec3 emission_color = vec3(0.0);
            uniform float emission_energy = 0.0;

            uniform bool melt_enabled = true;
            uniform bool melt_tactical = true;
            uniform vec3 melt_camera;
            uniform vec3 melt_target_a;
            uniform vec3 melt_target_b;
            uniform float melt_radius = 2.0;
            uniform float melt_edge = 0.07;
            uniform float tactical_cut_height = -100.0;

            varying vec3 world_position;
            varying vec3 world_normal;

            vec3 triplanar_weights(vec3 normal) {
                vec3 weights = pow(
                    max(abs(normal), vec3(0.0001)),
                    vec3(max(0.5, triplanar_sharpness)));
                return weights / max(weights.x + weights.y + weights.z, 0.0001);
            }

            vec4 sample_triplanar(sampler2D source, vec3 position, vec3 normal) {
                vec3 coordinate = position * triplanar_scale + triplanar_offset;
                vec3 weights = triplanar_weights(normal);
                vec4 x_projection = texture(source, coordinate.zy);
                vec4 y_projection = texture(source, coordinate.xz);
                vec4 z_projection = texture(source, coordinate.xy);
                return x_projection * weights.x +
                    y_projection * weights.y +
                    z_projection * weights.z;
            }

            float ordered_dither(vec2 pixel) {
                vec2 cell = mod(floor(pixel), 4.0);
                float row0 = cell.x < 1.0 ? 0.0 : cell.x < 2.0 ? 8.0 : cell.x < 3.0 ? 2.0 : 10.0;
                float row1 = cell.x < 1.0 ? 12.0 : cell.x < 2.0 ? 4.0 : cell.x < 3.0 ? 14.0 : 6.0;
                float row2 = cell.x < 1.0 ? 3.0 : cell.x < 2.0 ? 11.0 : cell.x < 3.0 ? 1.0 : 9.0;
                float row3 = cell.x < 1.0 ? 15.0 : cell.x < 2.0 ? 7.0 : cell.x < 3.0 ? 13.0 : 5.0;
                float value = cell.y < 1.0 ? row0 : cell.y < 2.0 ? row1 : cell.y < 3.0 ? row2 : row3;
                return (value + 0.5) / 16.0;
            }

            float segment_distance(vec3 point, vec3 start, vec3 finish) {
                vec3 segment = finish - start;
                float position = clamp(
                    dot(point - start, segment) / max(dot(segment, segment), 0.0001),
                    0.0,
                    1.0);
                return length(point - (start + segment * position));
            }

            float target_melt(vec3 point, vec3 target) {
                float tunnel = segment_distance(point, melt_camera, target);
                float bubble = length(point - target);
                float distance_to_opening = min(tunnel, bubble);
                return 1.0 - smoothstep(
                    max(0.05, melt_radius - melt_edge),
                    melt_radius + melt_edge,
                    distance_to_opening);
            }

            void vertex() {
                world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
            }

            void fragment() {
                if (melt_enabled) {
                    float melt = max(
                        target_melt(world_position, melt_target_a),
                        target_melt(world_position, melt_target_b));
                    if (melt_tactical && tactical_cut_height > -99.0) {
                        float roof_slice = smoothstep(
                            tactical_cut_height - 0.06,
                            tactical_cut_height + 0.06,
                            world_position.y);
                        melt = max(melt, roof_slice);
                    }
                    float dither = ordered_dither(FRAGCOORD.xy);
                    if (melt > dither) {
                        discard;
                    }
                }

                vec4 sampled_albedo = use_albedo_texture
                    ? (use_triplanar
                        ? sample_triplanar(albedo_texture, world_position, world_normal)
                        : texture(albedo_texture, UV))
                    : vec4(1.0);
                if (sampled_albedo.a * albedo_color.a < alpha_scissor_threshold) {
                    discard;
                }
                ALBEDO = sampled_albedo.rgb * albedo_color.rgb;
                ROUGHNESS = roughness_value;
                METALLIC = metallic_value;
                if (use_normal_texture) {
                    NORMAL_MAP = use_triplanar
                        ? sample_triplanar(normal_texture, world_position, world_normal).rgb
                        : texture(normal_texture, UV).rgb;
                    NORMAL_MAP_DEPTH = normal_scale;
                }
                vec3 sampled_emission = use_emission_texture
                    ? texture(emission_texture, UV).rgb
                    : vec3(1.0);
                EMISSION = sampled_emission * emission_color * emission_energy;
            }
            """,
    };

    private static Texture2D SolidTexture(Color color)
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(color);
        return ImageTexture.CreateFromImage(image);
    }

    private bool Occludes(Aabb bounds, Vector3 target, Camera3D camera)
    {
        if (camera.IsPositionBehind(target))
            return false;
        var forward = -camera.GlobalBasis.Z;
        var targetDepth = (target - camera.GlobalPosition).Dot(forward);
        var center = bounds.GetCenter();
        var extent = bounds.Size * Fo1CaveCutawayNumericContracts.GeometryFloat0Point5f;
        var centerDepth = (center - camera.GlobalPosition).Dot(forward);
        var depthRadius = MathF.Abs(forward.X) * extent.X +
            MathF.Abs(forward.Y) * extent.Y + MathF.Abs(forward.Z) * extent.Z;
        if (centerDepth - depthRadius >= targetDepth - _profile.MinimumTargetDepthMarginMeters)
            return false;

        var corners = new List<Vector2>();
        foreach (var x in new[] { bounds.Position.X, bounds.End.X })
            foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                {
                    var corner = new Vector3(x, y, z);
                    if (!camera.IsPositionBehind(corner))
                        corners.Add(camera.UnprojectPosition(corner));
                }
        if (corners.Count == 0)
            return false;
        var minimum = corners.Aggregate((one, two) => one.Min(two));
        var maximum = corners.Aggregate((one, two) => one.Max(two));
        var targetScreen = camera.UnprojectPosition(target);
        return targetScreen.X >= minimum.X - _profile.ScreenMarginPixels &&
            targetScreen.X <= maximum.X + _profile.ScreenMarginPixels &&
            targetScreen.Y >= minimum.Y - _profile.ScreenMarginPixels &&
            targetScreen.Y <= maximum.Y + _profile.ScreenMarginPixels;
    }

    private bool CrowdsTarget(Aabb bounds, Vector3 target)
    {
        return target.X >= bounds.Position.X - _profile.CameraClearanceMeters &&
            target.X <= bounds.End.X + _profile.CameraClearanceMeters &&
            target.Z >= bounds.Position.Z - _profile.CameraClearanceMeters &&
            target.Z <= bounds.End.Z + _profile.CameraClearanceMeters;
    }

    private static Aabb WorldBounds(Node3D root)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var count = 0;
        foreach (var mesh in Meshes(root))
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
            throw new InvalidOperationException($"Fallout cave melt entry has no meshes: {root.Name}");
        return new Aabb(minimum, maximum - minimum);
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node3D root) =>
        root is MeshInstance3D rootMesh
            ? Descendants<MeshInstance3D>(root).Prepend(rootMesh)
            : Descendants<MeshInstance3D>(root);

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

    private readonly record struct Entry(Node3D Root, Aabb Bounds);
}
