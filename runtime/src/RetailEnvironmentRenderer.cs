using Godot;

namespace OpenNV.Runtime;

internal static class RetailEnvironmentRenderer
{
    private const string WeatherCloudLayerGeometrySemantic = "weather-cloud-layer-geometry";
    private const string AtmosphereShaderSource = """
        shader_type spatial;
        render_mode unshaded, blend_mix, cull_back, depth_draw_never, fog_disabled;

        uniform vec3 sky_upper_encoded;
        uniform vec3 sky_lower_encoded;
        uniform vec3 horizon_encoded;
        uniform float rgb_multiplier;

        varying vec4 source_vertex_color;

        void vertex() {
            source_vertex_color = COLOR;
            vec3 world_direction = normalize(mat3(MODEL_MATRIX) * VERTEX);
            POSITION = PROJECTION_MATRIX * vec4(mat3(VIEW_MATRIX) * world_direction, 1.0);
            POSITION.z = 0.0;
        }

        void fragment() {
            // SKY.vso weights the three live WTHR constants by the authored
            // D3DCOLOR channels in atmosphere.nif. SKY.pso then multiplies RGB
            // by Params.y and preserves the authored alpha fade.
            vec3 encoded_color =
                horizon_encoded * source_vertex_color.r +
                sky_lower_encoded * source_vertex_color.g +
                sky_upper_encoded * source_vertex_color.b;
            ALBEDO = encoded_color * rgb_multiplier;
            ALPHA = source_vertex_color.a;
        }
        """;

    private const string CloudShaderSource = """
        shader_type spatial;
        render_mode unshaded, blend_mix, cull_back, depth_draw_never, fog_disabled;

        uniform sampler2D cloud_map : filter_linear_mipmap_anisotropic, repeat_enable;
        uniform vec3 cloud_color_encoded;
        uniform vec3 sky_lower_encoded;
        uniform vec3 sky_upper_encoded;
        uniform float rgb_multiplier;
        uniform float uv_offset_y;
        uniform float encoded_cutoff;
        uniform float transfer_linear_scale;
        uniform float transfer_offset;
        uniform float transfer_normalization;
        uniform float transfer_exponent;

        varying vec4 source_vertex_color;

        vec3 encoded_to_linear(vec3 encoded_color) {
            vec3 linear_segment = encoded_color / transfer_linear_scale;
            vec3 power_segment = pow(
                (encoded_color + vec3(transfer_offset)) / transfer_normalization,
                vec3(transfer_exponent));
            return mix(
                power_segment,
                linear_segment,
                lessThanEqual(encoded_color, vec3(encoded_cutoff)));
        }

        void vertex() {
            source_vertex_color = COLOR;
            vec3 world_direction = normalize(mat3(MODEL_MATRIX) * VERTEX);
            POSITION = PROJECTION_MATRIX * vec4(mat3(VIEW_MATRIX) * world_direction, 1.0);
            POSITION.z = 0.0;
        }

        void fragment() {
            vec4 source = texture(cloud_map, UV + vec2(0.0, uv_offset_y));
            // With the captured Params.x == 0 and identical TexMap/TexMapBlend
            // bindings, SKYTEX.pso reduces to this source sample and forces
            // alpha to zero only when the sampled red channel is exactly zero.
            if (source.r * source.r == 0.0) {
                source.a = 0.0;
            }
            vec3 encoded_tint =
                cloud_color_encoded * source_vertex_color.r +
                sky_lower_encoded * source_vertex_color.g +
                sky_upper_encoded * source_vertex_color.b;
            // The captured cloud sampler has sRGB sampling disabled.  Both
            // the sampled bytes and WTHR tint constants therefore remain in
            // the retail encoded domain until the image-space chain.
            ALBEDO = source.rgb * encoded_tint * rgb_multiplier;
            ALPHA = source.a * source_vertex_color.a;
        }
        """;

    internal static Application Apply(
        Node3D host,
        ActorReviewContract.EnvironmentState captured,
        ActorReviewBackground background,
        RuntimeConfiguration configuration) => Apply(
            host,
            captured,
            background.Content,
            background.Environment,
            configuration);

    internal static Application Apply(
        Node3D host,
        ActorReviewContract.EnvironmentState captured,
        CellContentLoader.LoadedContent content,
        RetailExteriorEnvironment environmentCatalog,
        RuntimeConfiguration configuration) => Apply(
            host,
            captured,
            content,
            environmentCatalog,
            null,
            configuration);

    internal static Application Apply(
        Node3D host,
        ActorReviewContract.EnvironmentState captured,
        CellContentLoader.LoadedContent content,
        RetailExteriorEnvironment environmentCatalog,
        GalleryRetailEvidence.DirectionalLightingReference? directionalLighting,
        RuntimeConfiguration configuration)
    {
        var resolved = environmentCatalog.Resolve(captured);
        if (directionalLighting is { } observedLighting)
            resolved = resolved with
            {
                AmbientEncoded = observedLighting.AmbientColorEncoded,
                SunlightEncoded = observedLighting.DiffuseColorEncoded,
            };
        var transfer = configuration.ActorCompiler.FaceGenMaterial.RuntimeAlbedoTransfer;
        // WTHR/XCLL colors are normalized constant values, not sampled image
        // data. The captured retail texture sRGB state therefore does not apply
        // to these three lighting constants; applying the FaceGen texture
        // transfer here over-saturates the directional light and washes the
        // authored road/ground response toward yellow.
        var ambient = resolved.AmbientEncoded;
        var sunlight = resolved.SunlightEncoded;
        var fog = resolved.FogEncoded;
        var imageSpaceConfiguration = configuration.FalloutEnvironment.ImageSpace;
        var sunlightDimmerIndex = imageSpaceConfiguration.TraitIndices.SunlightDimmer;
        if (sunlightDimmerIndex >= resolved.ImageSpace.Traits.Count)
            throw new InvalidOperationException(
                "Configured sunlight dimmer is outside the composed image-space traits.");
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = fog,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ambient,
            AmbientLightEnergy = configuration.Renderer.AmbientEnergyScale,
            TonemapMode = RuntimeRendering.ParseToneMapper(configuration.Renderer.ToneMapper),
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = fog,
            FogLightEnergy = configuration.Renderer.FogLightEnergy,
            FogDensity = configuration.Renderer.FogDensity,
            FogDepthBegin = resolved.FogNearGameUnits * configuration.World.GameUnitsToMeters,
            FogDepthEnd = resolved.FogFarGameUnits * configuration.World.GameUnitsToMeters,
            FogDepthCurve = resolved.FogPower,
        };
        var worldEnvironment = new WorldEnvironment
        {
            Name = $"WTHR_{resolved.WeatherFormId:X8}_Environment",
            Environment = environment,
        };
        host.AddChild(worldEnvironment);
        var directionalLight = new DirectionalLight3D
        {
            Name = $"WTHR_{resolved.WeatherFormId:X8}_Sunlight",
            LightColor = sunlight,
            LightEnergy = configuration.Renderer.DirectionalEnergyScale *
                resolved.ImageSpace.Traits[sunlightDimmerIndex],
            ShadowEnabled = configuration.ActorReview.DirectionalShadows,
        };
        if (directionalLighting is { } exactDirectionalLighting)
            directionalLight.Transform = new Transform3D(
                RetailLighting.DirectionalLightBasis(
                    exactDirectionalLighting.SurfaceToLightGodot),
                Vector3.Zero);
        else
            directionalLight.RotationDegrees =
                configuration.ActorReview.DirectionalRotationDegrees.Vector3();
        host.AddChild(directionalLight);
        var retailRoadMaterials = RuntimeMaterialLoader.ApplyRetailAmbientDirectionalLighting(
            content.Root,
            ambient,
            fog,
            resolved.FogNearGameUnits,
            resolved.FogFarGameUnits,
            resolved.FogPower,
            configuration.World.GameUnitsToMeters);
        var retailLandscapeMaterials = RuntimeMaterialLoader.ApplyRetailLandscapeLighting(
            content.Root,
            ambient,
            fog,
            resolved.FogNearGameUnits,
            resolved.FogFarGameUnits,
            resolved.FogPower,
            configuration.World.GameUnitsToMeters);
        var retailGrassMaterials = RuntimeMaterialLoader.ApplyRetailGrassLighting(
            content.Root,
            ambient,
            fog,
            resolved.FogNearGameUnits,
            resolved.FogFarGameUnits,
            resolved.FogPower,
            configuration.World.GameUnitsToMeters);
        RuntimeMaterialLoader.ApplyRetailGrassDistanceScale(
            content.Root,
            configuration.World.GameUnitsToMeters);
        var retailActorMaterials = RuntimeMaterialLoader.ApplyRetailActorLighting(
            host,
            ambient,
            fog,
            resolved.FogNearGameUnits,
            resolved.FogFarGameUnits,
            resolved.FogPower,
            configuration.World.GameUnitsToMeters);

        var atmosphere = AddAtmosphere(host, environmentCatalog, resolved, configuration);
        var clouds = AddClouds(host, environmentCatalog, resolved, configuration);
        var imageSpace = RetailImageSpaceRenderer.Apply(
            worldEnvironment,
            resolved.ImageSpace,
            imageSpaceConfiguration,
            configuration.Capture,
            configuration.ActorCompiler.FaceGenMaterial.RuntimeAlbedoTransfer);
        return new Application(
            resolved,
            atmosphere.SourceSha256,
            clouds.SourceSha256,
            clouds.Layers,
            imageSpace,
            true,
            true,
            false,
            false,
            directionalLighting is not null,
            directionalLighting,
            directionalLight.RotationDegrees,
            directionalLight.ShadowEnabled,
            retailRoadMaterials,
            retailRoadMaterials > 0,
            retailLandscapeMaterials,
            retailLandscapeMaterials > 0,
            retailGrassMaterials,
            retailGrassMaterials > 0,
            retailActorMaterials,
            retailActorMaterials > 0);
    }

    internal static SkyApplication AddSky(
        Node3D host,
        RetailExteriorEnvironment environment,
        RetailExteriorEnvironment.ResolvedEnvironment resolved,
        RuntimeConfiguration configuration)
    {
        var atmosphere = AddAtmosphere(host, environment, resolved, configuration);
        var clouds = AddClouds(host, environment, resolved, configuration);
        return new SkyApplication(
            atmosphere.SourceSha256,
            clouds.SourceSha256,
            clouds.Layers);
    }

    private static VerifiedGltfLoader.LoadedGltf AddAtmosphere(
        Node3D host,
        RetailExteriorEnvironment environment,
        RetailExteriorEnvironment.ResolvedEnvironment resolved,
        RuntimeConfiguration configuration)
    {
        var evidence = environment.SkyModels["atmosphere"];
        var loaded = VerifiedGltfLoader.Load(evidence.ModelPath, evidence.SidecarPath);
        var meshes = Descendants<MeshInstance3D>(loaded.Scene).ToArray();
        if (meshes.Length != 1 || meshes[0].Mesh is null ||
            meshes[0].Mesh!.GetSurfaceCount() != evidence.Surfaces.Count)
            throw new InvalidOperationException(
                "Owned atmosphere glTF differs from its compiled surface contract.");
        var mesh = meshes[0];
        var traitIndex = configuration.FalloutEnvironment.SkyRgbMultiplierImageSpaceTraitIndex;
        if (traitIndex >= resolved.ImageSpace.Traits.Count)
            throw new InvalidOperationException(
                "Configured sky RGB multiplier trait is outside the owned IMGS array.");
        var shader = new ShaderMaterial
        {
            Shader = new Shader { Code = AtmosphereShaderSource },
            RenderPriority = configuration.FalloutEnvironment.AtmosphereRenderPriority,
        };
        shader.SetShaderParameter("sky_upper_encoded", Rgb(resolved.SkyUpperEncoded));
        shader.SetShaderParameter("sky_lower_encoded", Rgb(resolved.SkyLowerEncoded));
        shader.SetShaderParameter("horizon_encoded", Rgb(resolved.HorizonEncoded));
        shader.SetShaderParameter("rgb_multiplier", resolved.ImageSpace.Traits[traitIndex]);
        mesh.SetSurfaceOverrideMaterial(evidence.Surfaces[0].Index, shader);
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        loaded.Scene.Name = $"WRLD_{environment.WorldspaceFormId:X8}_Atmosphere";
        host.AddChild(loaded.Scene);
        return loaded;
    }

    private static CloudApplication AddClouds(
        Node3D host,
        RetailExteriorEnvironment environment,
        RetailExteriorEnvironment.ResolvedEnvironment resolved,
        RuntimeConfiguration configuration)
    {
        var evidence = environment.SkyModels["clouds"];
        var loaded = VerifiedGltfLoader.Load(evidence.ModelPath, evidence.SidecarPath);
        var meshes = Descendants<MeshInstance3D>(loaded.Scene).ToArray();
        if (meshes.Length != 1 || meshes[0].Mesh is null ||
            meshes[0].Mesh!.GetSurfaceCount() != evidence.Surfaces.Count)
            throw new InvalidOperationException(
                "Owned cloud glTF differs from its compiled surface contract.");
        var mesh = meshes[0];
        var traitIndex = configuration.FalloutEnvironment.SkyRgbMultiplierImageSpaceTraitIndex;
        if (traitIndex >= resolved.ImageSpace.Traits.Count)
            throw new InvalidOperationException(
                "Configured sky RGB multiplier trait is outside the owned IMGS array.");
        var transfer = configuration.ActorCompiler.FaceGenMaterial.RuntimeAlbedoTransfer;
        var layers = new List<CloudLayerApplication>();
        var cloudSurface = evidence.Surfaces.Single(
            surface => surface.Semantic == WeatherCloudLayerGeometrySemantic);
        var cloudRoot = new Node3D
        {
            Name = $"WTHR_{resolved.WeatherFormId:X8}_Clouds",
        };
        for (var layer = 0; layer < resolved.CloudTextures.Count; ++layer)
        {
            var texture = resolved.CloudTextures[layer];
            if (texture is null)
            {
                layers.Add(new CloudLayerApplication(layer, cloudSurface.Name, null, false));
                continue;
            }
            var material = new ShaderMaterial
            {
                Shader = new Shader { Code = CloudShaderSource },
                RenderPriority = configuration.FalloutEnvironment.CloudRenderPriority,
            };
            material.SetShaderParameter("cloud_map", LoadTexture(texture.Value));
            material.SetShaderParameter(
                "cloud_color_encoded",
                Rgb(resolved.CloudColorsEncoded[layer]));
            material.SetShaderParameter("sky_lower_encoded", Rgb(resolved.SkyLowerEncoded));
            material.SetShaderParameter("sky_upper_encoded", Rgb(resolved.SkyUpperEncoded));
            material.SetShaderParameter("rgb_multiplier", resolved.ImageSpace.Traits[traitIndex]);
            material.SetShaderParameter("uv_offset_y", 0.0f);
            ApplyTransfer(material, transfer);
            var layerMesh = new MeshInstance3D
            {
                Name = $"WTHR_{resolved.WeatherFormId:X8}_CloudLayer_{layer}",
                Mesh = mesh.Mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            foreach (var surface in evidence.Surfaces)
                layerMesh.SetSurfaceOverrideMaterial(surface.Index, InvisibleMaterial());
            layerMesh.SetSurfaceOverrideMaterial(cloudSurface.Index, material);
            cloudRoot.AddChild(layerMesh);
            layers.Add(new CloudLayerApplication(
                layer,
                cloudSurface.Name,
                texture.Value.AuthoredPath,
                true));
        }
        loaded.Scene.Free();
        host.AddChild(cloudRoot);
        return new CloudApplication(loaded.SourceSha256, layers);
    }

    private static ShaderMaterial InvisibleMaterial() => new()
    {
        Shader = new Shader
        {
            Code = "shader_type spatial; render_mode unshaded; void fragment() { discard; }",
        },
    };

    private static Texture2D LoadTexture(RetailExteriorEnvironment.TextureEvidence evidence)
    {
        VerifiedGltfLoader.VerifyHash(evidence.PngPath, evidence.PngSha256);
        var image = Image.LoadFromFile(evidence.PngPath);
        if (image is null || image.IsEmpty())
            throw new InvalidOperationException(
                $"Godot could not load owned WTHR texture: {evidence.AuthoredPath}");
        return ImageTexture.CreateFromImage(image);
    }

    private static void ApplyTransfer(
        ShaderMaterial material,
        ColorTransferConfiguration transfer)
    {
        material.SetShaderParameter("encoded_cutoff", transfer.EncodedCutoff);
        material.SetShaderParameter("transfer_linear_scale", transfer.LinearScale);
        material.SetShaderParameter("transfer_offset", transfer.Offset);
        material.SetShaderParameter("transfer_normalization", transfer.Normalization);
        material.SetShaderParameter("transfer_exponent", transfer.Exponent);
    }

    private static Color EncodedToLinear(Color encoded, ColorTransferConfiguration transfer)
    {
        float Channel(float value) => value <= transfer.EncodedCutoff
            ? value / transfer.LinearScale
            : MathF.Pow(
                (value + transfer.Offset) / transfer.Normalization,
                transfer.Exponent);
        return new Color(Channel(encoded.R), Channel(encoded.G), Channel(encoded.B), encoded.A);
    }

    private static Vector3 Rgb(Color color) => new(color.R, color.G, color.B);

    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    internal readonly record struct Application(
        RetailExteriorEnvironment.ResolvedEnvironment Environment,
        string AtmosphereSourceSha256,
        string CloudsSourceSha256,
        IReadOnlyList<CloudLayerApplication> CloudLayers,
        RetailImageSpaceRenderer.Application ImageSpace,
        bool WeatherRecordApplied,
        bool ImageSpaceValidated,
        bool AuxiliaryCloudSurfacesResolved,
        bool CloudUvOffsetResolved,
        bool DirectionalVectorResolved,
        GalleryRetailEvidence.DirectionalLightingReference? DirectionalLighting,
        Vector3 DirectionalRotationDegrees,
        bool DirectionalShadowsEnabled,
        int RetailRoadMaterials,
        bool RetailRoadDiffuseCoreResolved,
        int RetailLandscapeMaterials,
        bool RetailLandscapeDiffuseCoreResolved,
        int RetailGrassMaterials,
        bool RetailGrassDiffuseCoreResolved,
        int RetailActorMaterials,
        bool RetailActorDiffuseCoreResolved);

    internal readonly record struct CloudApplication(
        string SourceSha256,
        IReadOnlyList<CloudLayerApplication> Layers);

    internal readonly record struct SkyApplication(
        string AtmosphereSourceSha256,
        string CloudsSourceSha256,
        IReadOnlyList<CloudLayerApplication> CloudLayers);

    internal readonly record struct CloudLayerApplication(
        int WeatherLayerIndex,
        string SurfaceName,
        string? TexturePath,
        bool Visible);
}
