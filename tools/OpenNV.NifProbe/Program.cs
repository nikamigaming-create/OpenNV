using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

const uint ShaderFlagExternalEmittance = 1U << 29;
const uint ShaderFlagSpecular = 1U << 0;
const uint ShaderFlagVertexAlpha = 1U << 3;
const uint ShaderFlagEnvironmentMapping = 1U << 7;
const uint ShaderFlagRemappableTextures = 1U << 25;
const uint ShaderFlagDecal = 1U << 26;
const uint ShaderFlagDynamicDecal = 1U << 27;
const uint ShaderFlagZBufferTest = 1U << 31;
const uint ShaderFlagZBufferWrite = 1U << 0;
const uint ShaderFlagNoFade = 1U << 3;
const uint ReceptionUnlitFlags = ShaderFlagVertexAlpha | ShaderFlagRemappableTextures |
    ShaderFlagDecal | ShaderFlagDynamicDecal | ShaderFlagZBufferTest;
const uint TerminalUnlitFlags = ShaderFlagSpecular | ShaderFlagRemappableTextures | ShaderFlagZBufferTest;
const int ExpectedEmissiveTextureSets = 2;
const int ExpectedNormalAlphaEnvironmentSets = 9;
const int ExpectedReceptionUnlitShaders = 3;
const int ExpectedTerminalUnlitShaders = 1;
const int EmissiveTextureSlot = 2;
const int EnvironmentTextureSlot = 4;
const int EnvironmentMaskTextureSlot = 5;
const uint UnsupportedBsxFlag = 1U << 4;

if (args.Length == 1 && args[0] == "--bsx-synthetic")
{
    var noEvidence = new FalloutNifBsxEvidence(false, false, false, false, false);
    var fullEvidence = new FalloutNifBsxEvidence(true, true, true, true, true);
    FalloutNifBsxContract.Validate(0, noEvidence);
    FalloutNifBsxContract.Validate(FalloutNifBsxContract.Animated, noEvidence);
    FalloutNifBsxContract.Validate(
        FalloutNifBsxContract.Havok | FalloutNifBsxContract.Complex |
        FalloutNifBsxContract.Dynamic | FalloutNifBsxContract.Ragdoll |
        FalloutNifBsxContract.Articulated,
        fullEvidence);
    FalloutNifBsxContract.Validate(FalloutNifBsxContract.EditorMarkers, fullEvidence);
    FalloutNifBsxContract.Validate(FalloutNifBsxContract.ExternalEmit, fullEvidence);
    RequireRejectedBsx(FalloutNifBsxContract.Havok, noEvidence);
    RequireRejectedBsx(FalloutNifBsxContract.Complex, noEvidence);
    RequireRejectedBsx(FalloutNifBsxContract.Dynamic, noEvidence);
    RequireRejectedBsx(FalloutNifBsxContract.EditorMarkers, noEvidence);
    RequireRejectedBsx(FalloutNifBsxContract.ExternalEmit, noEvidence);
    RequireRejectedBsx(FalloutNifBsxContract.Ragdoll, noEvidence);
    RequireRejectedBsx(FalloutNifBsxContract.Articulated, noEvidence);
    RequireRejectedBsx(UnsupportedBsxFlag, fullEvidence);
    Console.WriteLine("OPENNV_NIF_BSX_SYNTHETIC_OK accepted=5 rejected=8");
    return 0;
}

if (args.Length == 2 && args[0] == "--shader-contract")
{
    try
    {
        var manifest = Path.GetFullPath(args[1]);
        var manifestBytes = File.ReadAllBytes(manifest);
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        RuntimeOwnedContentSource.Configure(
            root.GetProperty("roots")[0].GetProperty("root").GetString()!,
            manifest,
            Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
            root.GetProperty("stackId").GetString());
        string[] paths =
        [
            @"meshes\clutter\lab\chemistryexperiment01.nif",
            @"meshes\clutter\lab\hotplate01.nif",
            @"meshes\dlc04\clutter\lights\dlc04lightceiling01off.nif",
            @"meshes\dungeons\vaultruined\clutter\lampgenericr01.nif",
            @"meshes\clutter\office\officereceptiondeskendcapl01.nif",
            @"meshes\clutter\office\officereceptiondeskendcapr01.nif",
            @"meshes\clutter\office\officereceptiondeskmid01.nif",
            @"meshes\dungeons\vaultruined\clutter\terminaldeskr01.nif",
        ];
        var lightingCount = 0;
        var noLightingCount = 0;
        var textureSetCount = 0;
        var emissiveTextureSets = 0;
        var normalAlphaEnvironmentSets = 0;
        var receptionUnlitShaders = 0;
        var terminalUnlitShaders = 0;
        foreach (var path in paths)
        {
            var source = RuntimeOwnedContentSource.Current!;
            if (!source.TryRead(path, null, out var payload, out _))
                throw new FileNotFoundException($"Shader audit member is missing: {path}");
            var file = FalloutNifFile.Read(payload);
            foreach (var block in file.Blocks)
            {
                if (block.TypeName == "BSShaderPPLightingProperty")
                {
                    var shader = (FalloutNifShaderProperty)file.ReadObject(block.Index);
                    lightingCount++;
                    var textures = (FalloutNifShaderTextureSet)file.ReadObject(shader.TextureSet);
                    if (!string.IsNullOrEmpty(textures.Textures[EmissiveTextureSlot]))
                        emissiveTextureSets++;
                    if ((shader.ShaderFlags & ShaderFlagEnvironmentMapping) != 0 &&
                        !string.IsNullOrEmpty(textures.Textures[EnvironmentTextureSlot]) &&
                        string.IsNullOrEmpty(textures.Textures[EnvironmentMaskTextureSlot]))
                        normalAlphaEnvironmentSets++;
                    Console.WriteLine($"OPENNV_NIF_SHADER_LIGHTING path={path} block={block.Index} " +
                        $"type={shader.ShaderType} flags1=0x{shader.ShaderFlags:x8} " +
                        $"flags2=0x{shader.ShaderFlags2:x8} textures={shader.TextureSet}");
                }
                else if (block.TypeName == "BSShaderNoLightingProperty")
                {
                    var shader = (FalloutNifNoLightingProperty)file.ReadObject(block.Index);
                    noLightingCount++;
                    if (shader.ShaderFlags == ReceptionUnlitFlags &&
                        shader.ShaderFlags2 == (ShaderFlagZBufferWrite | ShaderFlagNoFade) &&
                        string.IsNullOrEmpty(shader.FileName))
                        receptionUnlitShaders++;
                    if (shader.ShaderFlags == TerminalUnlitFlags &&
                        shader.ShaderFlags2 == ShaderFlagZBufferWrite &&
                        shader.FileName.Equals(
                            @"textures\dungeons\vaultruined\TerminalScreenR01.dds",
                            StringComparison.OrdinalIgnoreCase))
                        terminalUnlitShaders++;
                    Console.WriteLine($"OPENNV_NIF_SHADER_UNLIT path={path} block={block.Index} " +
                        $"type={shader.ShaderType} flags1=0x{shader.ShaderFlags:x8} " +
                        $"flags2=0x{shader.ShaderFlags2:x8} clamp={shader.TextureClampMode} " +
                        $"file={shader.FileName} falloff={shader.FalloffStartAngle:R}/" +
                        $"{shader.FalloffStopAngle:R}/{shader.FalloffStartOpacity:R}/" +
                        $"{shader.FalloffStopOpacity:R}");
                }
                else if (block.TypeName == "BSShaderTextureSet")
                {
                    var textures = (FalloutNifShaderTextureSet)file.ReadObject(block.Index);
                    textureSetCount++;
                    Console.WriteLine($"OPENNV_NIF_SHADER_TEXTURES path={path} block={block.Index} " +
                        $"slots=[{string.Join(',', textures.Textures.Select((value, index) => $"{index}:{value}"))}]");
                }
            }
        }
        if (emissiveTextureSets != ExpectedEmissiveTextureSets ||
            normalAlphaEnvironmentSets != ExpectedNormalAlphaEnvironmentSets ||
            receptionUnlitShaders != ExpectedReceptionUnlitShaders ||
            terminalUnlitShaders != ExpectedTerminalUnlitShaders)
            throw new InvalidDataException(
                $"Owned shader contract counts changed: emissive={emissiveTextureSets} " +
                $"normalAlphaEnvironment={normalAlphaEnvironmentSets} " +
                $"receptionUnlit={receptionUnlitShaders} terminalUnlit={terminalUnlitShaders}.");
        Console.WriteLine($"OPENNV_NIF_SHADER_CONTRACT_OK models={paths.Length} " +
            $"lighting={lightingCount} unlit={noLightingCount} textureSets={textureSetCount} " +
            $"emissive={emissiveTextureSets} normalAlphaEnvironment={normalAlphaEnvironmentSets} " +
            $"receptionUnlit={receptionUnlitShaders} terminalUnlit={terminalUnlitShaders}");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_NIF_SHADER_CONTRACT_FAIL {error.GetType().Name}: {error.Message}");
        return 1;
    }
}

if (args.Length == 2 && args[0] == "--controller-audit")
{
    try
    {
        var manifest = Path.GetFullPath(args[1]);
        var manifestBytes = File.ReadAllBytes(manifest);
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        RuntimeOwnedContentSource.Configure(
            root.GetProperty("roots")[0].GetProperty("root").GetString()!,
            manifest,
            Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
            root.GetProperty("stackId").GetString());
        string[] paths =
        [
            @"meshes\clutter\ammo\ammobox01.nif",
            @"meshes\clutter\fridge\fridgeclosed01.nif",
            @"meshes\clutter\health\firstaidkit01.nif",
            @"meshes\clutter\lab\chemistryexperiment01.nif",
            @"meshes\dungeons\caves\lamplight\lamplightchandelier.nif",
            @"meshes\dungeons\vaultruined\clutter\terminaldeskr01.nif",
            @"meshes\dungeons\nv_craftsmanhomesinterior\nvcraftsmanrmdooranimated.nif",
        ];
        var sequenceCount = 0;
        var textureControllers = 0;
        var materialControllers = 0;
        var directTransforms = 0;
        var floatKeys = 0;
        var pointKeys = 0;
        foreach (var path in paths)
        {
            var source = RuntimeOwnedContentSource.Current!;
            if (!source.TryRead(path, null, out var payload, out _))
                throw new FileNotFoundException($"Controller audit member is missing: {path}");
            var file = FalloutNifFile.Read(payload);
            Console.WriteLine($"OPENNV_NIF_CONTROLLER_MODEL path={path} blocks={file.Blocks.Count} " +
                $"types={string.Join(',', file.Blocks.Select(block => $"{block.Index}:{block.TypeName}:{block.Size}"))}");
            foreach (var block in file.Blocks)
            {
                if (block.TypeName == "NiControllerManager")
                {
                    var manager = (FalloutNifControllerManager)file.ReadObject(block.Index);
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_MANAGER path={path} block={block.Index} " +
                        $"time={FormatTime(manager.Time)} cumulative={manager.Cumulative} " +
                        $"sequences={string.Join(',', manager.Sequences)} palette={manager.ObjectPalette}");
                }
                else if (block.TypeName == "NiMultiTargetTransformController")
                {
                    var multi = (FalloutNifMultiTargetTransformController)file.ReadObject(block.Index);
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_MULTI path={path} block={block.Index} " +
                        $"time={FormatTime(multi.Time)} targets={string.Join(',', multi.ExtraTargets)}");
                }
                else if (block.TypeName == "NiControllerSequence")
                {
                    var sequence = file.ReadControllerSequence(block.Index);
                    sequenceCount++;
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_SEQUENCE path={path} block={block.Index} " +
                        $"name={sequence.Name} cycle={sequence.CycleType} frequency={sequence.Frequency:R} " +
                        $"start={sequence.StartTime:R} stop={sequence.StopTime:R} weight={sequence.Weight:R} " +
                        $"target={sequence.TargetName} notes={sequence.AnimationNotes} " +
                        $"unknown={sequence.UnknownShort} manager={sequence.Manager}");
                    foreach (var link in sequence.ControlledBlocks)
                        Console.WriteLine($"OPENNV_NIF_CONTROLLER_LINK path={path} sequence={block.Index} " +
                            $"node={link.NodeName} property={link.PropertyType} type={link.ControllerType} " +
                            $"variable1={link.Variable1} variable2={link.Variable2} " +
                            $"interpolator={link.Interpolator} controller={link.Controller} priority={link.Priority}");
                }
                else if (block.TypeName == "NiTransformInterpolator")
                {
                    var interpolator = (FalloutNifTransformInterpolator)file.ReadObject(block.Index);
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_TRANSFORM path={path} block={block.Index} " +
                        $"translation={interpolator.Translation} rotation={interpolator.Rotation} " +
                        $"scale={interpolator.Scale:R} data={interpolator.Data}");
                }
                else if (block.TypeName == "NiTransformData")
                {
                    var data = (FalloutNifTransformData)file.ReadObject(block.Index);
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_DATA path={path} block={block.Index} " +
                        $"rotationType={data.RotationType} quaternion={data.QuaternionRotations.Length} " +
                        $"xyz={string.Join(',', data.XyzRotations.Select(axis => axis.Length))} " +
                        $"translation={data.Translations.Length} scale={data.Scales.Length}");
                }
                else if (block.TypeName == "NiFloatInterpolator")
                {
                    var interpolator = (FalloutNifFloatInterpolator)file.ReadObject(block.Index);
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_FLOAT path={path} block={block.Index} " +
                        $"value={interpolator.Value:R} data={interpolator.Data}");
                }
                else if (block.TypeName == "NiFloatData")
                {
                    var data = (FalloutNifFloatData)file.ReadObject(block.Index);
                    floatKeys += data.Keys.Length;
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_FLOAT_DATA path={path} block={block.Index} " +
                        $"keys={FormatScalarKeys(data.Keys)}");
                }
                else if (block.TypeName == "NiPoint3Interpolator")
                {
                    var interpolator = (FalloutNifPoint3Interpolator)file.ReadObject(block.Index);
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_POINT3 path={path} block={block.Index} " +
                        $"value={interpolator.Value} data={interpolator.Data}");
                }
                else if (block.TypeName == "NiPosData")
                {
                    var data = (FalloutNifPositionData)file.ReadObject(block.Index);
                    pointKeys += data.Keys.Length;
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_POINT3_DATA path={path} block={block.Index} " +
                        $"keys={FormatVectorKeys(data.Keys)}");
                }
                else if (block.TypeName == "NiBlendFloatInterpolator")
                {
                    var blend = (FalloutNifBlendFloatInterpolator)file.ReadObject(block.Index);
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_BLEND_FLOAT path={path} block={block.Index} " +
                        $"flags=0x{blend.Flags:x2} size={blend.ArraySize} " +
                        $"threshold={blend.WeightThreshold:R} value={blend.Value:R}");
                }
                else if (block.TypeName == "NiBlendPoint3Interpolator")
                {
                    var blend = (FalloutNifBlendPoint3Interpolator)file.ReadObject(block.Index);
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_BLEND_POINT3 path={path} block={block.Index} " +
                        $"flags=0x{blend.Flags:x2} size={blend.ArraySize} " +
                        $"threshold={blend.WeightThreshold:R} value={blend.Value}");
                }
                else if (block.TypeName == "NiTextureTransformController")
                {
                    var controller = (FalloutNifTextureTransformController)file.ReadObject(block.Index);
                    textureControllers++;
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_TEXTURE path={path} block={block.Index} " +
                        $"time={FormatTime(controller.Time)} shaderMap={controller.ShaderMap} " +
                        $"interpolator={controller.Interpolator} slot={controller.TextureSlot} " +
                        $"operation={controller.Operation}");
                }
                else if (block.TypeName == "NiMaterialColorController")
                {
                    var controller = (FalloutNifMaterialColorController)file.ReadObject(block.Index);
                    materialControllers++;
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_MATERIAL path={path} block={block.Index} " +
                        $"time={FormatTime(controller.Time)} interpolator={controller.Interpolator} " +
                        $"targetColor={controller.TargetColor}");
                }
                else if (block.TypeName == "NiTransformController")
                {
                    var controller = (FalloutNifTransformController)file.ReadObject(block.Index);
                    directTransforms++;
                    Console.WriteLine($"OPENNV_NIF_CONTROLLER_DIRECT_TRANSFORM path={path} block={block.Index} " +
                        $"time={FormatTime(controller.Time)} interpolator={controller.Interpolator}");
                }
            }
        }
        if (textureControllers != 4 || materialControllers != 4 ||
            floatKeys != 8 || pointKeys != 23 || directTransforms < 2)
            throw new InvalidDataException(
                "Registered controller corpus no longer matches the admitted float/point3 controller contract.");
        Console.WriteLine(
            $"OPENNV_NIF_CONTROLLER_OK models={paths.Length} sequences={sequenceCount} " +
            $"textureControllers={textureControllers} materialControllers={materialControllers} " +
            $"directTransforms={directTransforms} floatKeys={floatKeys} pointKeys={pointKeys}");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_NIF_ERROR {error.GetType().Name}: {error.Message}");
        return 1;
    }
    finally
    {
        RuntimeOwnedContentSource.Clear();
    }
}

if (args.Length == 2 && args[0] == "--bsx-contract")
{
    try
    {
        var manifest = Path.GetFullPath(args[1]);
        var manifestBytes = File.ReadAllBytes(manifest);
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        RuntimeOwnedContentSource.Configure(
            root.GetProperty("roots")[0].GetProperty("root").GetString()!,
            manifest,
            Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
            root.GetProperty("stackId").GetString());
        var expected = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            [@"meshes\clutter\bathroom\sinkmetalr01.nif"] =
                FalloutNifBsxContract.Havok | FalloutNifBsxContract.EditorMarkers,
            [@"meshes\effects\ambient\industrial\indfxlightraysnleftsm01.nif"] =
                FalloutNifBsxContract.EditorMarkers,
            [@"meshes\dlc04\clutter\lights\dlc04lightceiling01off.nif"] =
                FalloutNifBsxContract.Havok | FalloutNifBsxContract.Complex,
            [@"meshes\dlc04\clutter\lights\dlc04lightwall01off.nif"] =
                FalloutNifBsxContract.Havok | FalloutNifBsxContract.Complex,
        };
        var editorMarkers = 0;
        var complexCollision = 0;
        foreach (var (path, expectedFlags) in expected)
        {
            var source = RuntimeOwnedContentSource.Current!;
            if (!source.TryRead(path, null, out var payload, out _))
                throw new FileNotFoundException($"Targeted BSX member is missing: {path}");
            var file = FalloutNifFile.Read(payload);
            var flags = file.Blocks.Where(block => block.TypeName == "BSXFlags")
                .Select(block => ((FalloutNifBsxFlags)file.ReadObject(block.Index)).Flags)
                .Single();
            if (flags != expectedFlags)
                throw new InvalidDataException(
                    $"Targeted BSX flags changed for {path}: expected=0x{expectedFlags:x8} actual=0x{flags:x8}");
            FalloutNifBsxContract.Validate(
                flags,
                ReadBsxEvidence(file, ShaderFlagExternalEmittance));
            if ((flags & FalloutNifBsxContract.EditorMarkers) != 0)
                editorMarkers++;
            if ((flags & FalloutNifBsxContract.Complex) != 0)
                complexCollision++;
        }
        RequireRejectedBsx(
            FalloutNifBsxContract.EditorMarkers,
            new FalloutNifBsxEvidence(false, false, false, false, false));
        RequireRejectedBsx(
            FalloutNifBsxContract.Articulated,
            new FalloutNifBsxEvidence(true, false, false, false, false));
        RequireRejectedBsx(
            FalloutNifBsxContract.Havok,
            new FalloutNifBsxEvidence(false, false, false, false, false));
        RequireRejectedBsx(
            FalloutNifBsxContract.Dynamic,
            new FalloutNifBsxEvidence(false, false, false, false, false));
        RequireRejectedBsx(
            FalloutNifBsxContract.ExternalEmit,
            new FalloutNifBsxEvidence(false, false, false, false, false));
        FalloutNifBsxContract.Validate(
            FalloutNifBsxContract.Animated,
            new FalloutNifBsxEvidence(false, false, false, false, false));
        Console.WriteLine(
            $"OPENNV_NIF_BSX_OK models={expected.Count} editorMarkers={editorMarkers} " +
            $"complexCollision={complexCollision} negative=pass");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_NIF_ERROR {error.GetType().Name}: {error.Message}");
        return 1;
    }
    finally
    {
        RuntimeOwnedContentSource.Clear();
    }
}

if (args.Length == 2 && args[0] == "--bsx-audit")
{
    try
    {
        var manifest = Path.GetFullPath(args[1]);
        var manifestBytes = File.ReadAllBytes(manifest);
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        RuntimeOwnedContentSource.Configure(
            root.GetProperty("roots")[0].GetProperty("root").GetString()!,
            manifest,
            Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
            root.GetProperty("stackId").GetString());
        var expected = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            [@"meshes\clutter\hamradio\hamradio02.nif"] =
                FalloutNifBsxContract.Animated | FalloutNifBsxContract.Havok,
            [@"meshes\clutter\hamradio\hamradio03.nif"] =
                FalloutNifBsxContract.Animated | FalloutNifBsxContract.Havok,
            [@"meshes\clutter\junk\bucket01.nif"] =
                FalloutNifBsxContract.Havok | FalloutNifBsxContract.Complex |
                FalloutNifBsxContract.Dynamic | FalloutNifBsxContract.Articulated,
            [@"meshes\clutter\nv_fan.nif"] = FalloutNifBsxContract.Animated,
            [@"meshes\clutter\school\globe01.nif"] =
                FalloutNifBsxContract.Havok | FalloutNifBsxContract.Complex |
                FalloutNifBsxContract.Dynamic | FalloutNifBsxContract.Articulated,
            [@"meshes\dlc04\clutter\lights\dlc04lightwall01on.nif"] =
                FalloutNifBsxContract.Havok | FalloutNifBsxContract.Complex |
                FalloutNifBsxContract.ExternalEmit,
            [@"meshes\dungeons\caves\lamplight\lamplightchandelieroff.nif"] =
                FalloutNifBsxContract.Animated | FalloutNifBsxContract.Havok,
            [@"meshes\furniture\chair01.nif"] =
                FalloutNifBsxContract.Animated | FalloutNifBsxContract.Havok,
            [@"meshes\weapons\2handautomatic\9mmsmg.nif"] =
                FalloutNifBsxContract.Animated | FalloutNifBsxContract.Havok |
                FalloutNifBsxContract.Dynamic,
        };
        var acceptedModels = 0;
        var rejectedModels = 0;
        foreach (var (path, expectedFlags) in expected)
        {
            var source = RuntimeOwnedContentSource.Current!;
            if (!source.TryRead(path, null, out var payload, out _))
                throw new FileNotFoundException($"BSX audit member is missing: {path}");
            var file = FalloutNifFile.Read(payload);
            var flags = file.Blocks.Where(block => block.TypeName == "BSXFlags")
                .Select(block => ((FalloutNifBsxFlags)file.ReadObject(block.Index)).Flags)
                .Single();
            if (flags != expectedFlags)
                throw new InvalidDataException(
                    $"BSX audit flags changed for {path}: expected=0x{expectedFlags:x8} actual=0x{flags:x8}");
            var roots = file.Roots.Select(file.ReadNode).ToArray();
            var hasEditorMarker = file.Blocks
                .Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
                .Select(block => file.ReadNode(block.Index))
                .Any(node => node.Name.Equals("EditorMarker", StringComparison.Ordinal));
            var controllers = file.Blocks
                .Where(block => block.TypeName.EndsWith("Controller", StringComparison.Ordinal) ||
                    block.TypeName == "NiControllerManager")
                .Select(block => $"{block.Index}:{block.TypeName}")
                .ToArray();
            var materialControllers = file.Blocks
                .Where(block => block.TypeName == "NiMaterialColorController")
                .Select(block => (FalloutNifMaterialColorController)file.ReadObject(block.Index))
                .Select(value => $"{value.Block.Index}:{FormatTime(value.Time)}:" +
                    $"interpolator={FormatPointInterpolator(file, value.Interpolator)}:" +
                    $"targetColor={value.TargetColor}")
                .ToArray();
            var collision = file.Blocks
                .Where(block => block.TypeName.Contains("CollisionObject", StringComparison.Ordinal) ||
                    block.TypeName is "bhkRigidBody" or "bhkRigidBodyT")
                .Select(block => $"{block.Index}:{block.TypeName}")
                .ToArray();
            var textKeys = file.Blocks
                .Where(block => block.TypeName == "NiTextKeyExtraData")
                .Select(block => (FalloutNifTextKeyExtraData)file.ReadObject(block.Index))
                .SelectMany(value => value.Keys)
                .Select(value => $"{value.Time:R}:{value.Value}")
                .ToArray();
            var shaders = file.Blocks
                .Where(block => block.TypeName is "BSShaderPPLightingProperty" or
                    "BSShaderNoLightingProperty")
                .Select(block => file.ReadObject(block.Index))
                .Select(value => value switch
                {
                    FalloutNifShaderProperty shader =>
                        $"{shader.Block.Index}:lit:0x{shader.ShaderFlags:x8}:0x{shader.ShaderFlags2:x8}",
                    FalloutNifNoLightingProperty shader =>
                        $"{shader.Block.Index}:unlit:0x{shader.ShaderFlags:x8}:0x{shader.ShaderFlags2:x8}",
                    _ => throw new InvalidDataException("BSX audit lost a shader block."),
                })
                .ToArray();
            var accepted = true;
            try
            {
                FalloutNifBsxContract.Validate(
                    flags,
                    ReadBsxEvidence(file, ShaderFlagExternalEmittance));
            }
            catch (NotSupportedException)
            {
                accepted = false;
            }
            var expectedAccepted = true;
            if (accepted != expectedAccepted)
                throw new InvalidDataException(
                    $"BSX audit acceptance changed for {path}: expected={expectedAccepted} actual={accepted}");
            if (accepted)
                acceptedModels++;
            else
                rejectedModels++;
            Console.WriteLine($"OPENNV_NIF_BSX_AUDIT path={path} flags=0x{flags:x8} " +
                $"roots=[{string.Join(',', roots.Select(value =>
                    $"{value.Block.Index}:{value.Block.TypeName}:{value.Name}:controller={value.Controller}:" +
                    $"collision={value.CollisionObject}:extra={string.Join('|', value.ExtraData.Select(index =>
                        $"{index}:{file.Blocks[index].TypeName}"))}"))}] " +
                $"controllers=[{string.Join(',', controllers)}] collision=[{string.Join(',', collision)}] " +
                $"materialControllers=[{string.Join(',', materialControllers)}] " +
                $"textKeys=[{string.Join(',', textKeys)}] shaders=[{string.Join(',', shaders)}]");
        }
        Console.WriteLine($"OPENNV_NIF_BSX_AUDIT_OK models={expected.Count} " +
            $"accepted={acceptedModels} rejected={rejectedModels}");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_NIF_BSX_AUDIT_FAIL {error.GetType().Name}: {error.Message}");
        return 1;
    }
    finally
    {
        RuntimeOwnedContentSource.Clear();
    }
}

if (args.Length == 3 && args[0] == "--skin-contract")
{
    try
    {
        var archive = new FalloutBsaArchive(args[1]);
        var payload = archive.Read(args[2]);
        var file = FalloutNifFile.Read(payload);
        var geometries = file.Blocks
            .Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
            .Select(block => file.ReadGeometry(block.Index))
            .Where(geometry => geometry.SkinInstance != -1)
            .ToArray();
        if (geometries.Length != 1)
            throw new InvalidDataException(
                $"Targeted skin probe requires exactly one skinned geometry, found {geometries.Length}.");
        var geometry = geometries[0];
        var mesh = file.ReadMeshData(geometry.Data);
        var instance = file.ReadObject(geometry.SkinInstance) as FalloutNifSkinInstance ??
            throw new InvalidDataException("Geometry skin instance has an unexpected block type.");
        var data = file.ReadObject(instance.Data) as FalloutNifSkinData ??
            throw new InvalidDataException("Skin instance data has an unexpected block type.");
        var partition = file.ReadObject(instance.SkinPartition) as FalloutNifSkinPartition ??
            throw new InvalidDataException("Skin partition has an unexpected block type.");
        var binding = FalloutNifOneBoneSkin.Validate(instance, data, partition, mesh.Vertices.Length);
        if (binding.BoneIndices.Length != checked(mesh.Vertices.Length * 4) ||
            binding.Weights.Length != binding.BoneIndices.Length)
            throw new InvalidDataException("One-bone skin output arrays have unexpected lengths.");
        var invalidRows = partition.Partitions[0].VertexWeights
            .Select(row => (float[])row.Clone()).ToArray();
        invalidRows[0][0] *= 0.5f;
        var invalidBlock = partition.Partitions[0] with { VertexWeights = invalidRows };
        var invalidPartition = partition with { Partitions = [invalidBlock] };
        try
        {
            _ = FalloutNifOneBoneSkin.Validate(instance, data, invalidPartition, mesh.Vertices.Length);
            throw new InvalidDataException("Malformed skin weights did not fail closed.");
        }
        catch (InvalidDataException error) when (
            error.Message.Contains("non-normalized", StringComparison.Ordinal))
        {
        }
        var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        Console.WriteLine(
            $"OPENNV_NIF_SKIN_OK sha256={sha256} bytes={payload.Length} " +
            $"vertices={mesh.Vertices.Length} triangles={mesh.Triangles.Length} " +
            $"root={binding.SkeletonRoot} bone={binding.Bone} influences={binding.Weights.Length} " +
            "negative=pass");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_NIF_ERROR {error.GetType().Name}: {error.Message}");
        return 1;
    }
}

if (args.Length == 3 && args[0] == "--cell-audit")
{
    try
    {
        return AuditCell(args[1], args[2]);
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_NIF_ERROR {error.GetType().Name}: {error.Message}");
        return 1;
    }
}

if (args.Length == 2 && args[0] == "--directory")
{
    try
    {
        var paths = Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".nif" or ".kf")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var passed = 0;
        foreach (var path in paths)
        {
            try
            {
                _ = Probe(File.ReadAllBytes(path));
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"Corpus member failed: {path}: {error.Message}", error);
            }
            passed++;
        }
        Console.WriteLine($"OPENNV_NIF_CORPUS_OK files={passed}");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_NIF_ERROR {error.GetType().Name}: {error.Message}");
        return 1;
    }
}

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "usage: OpenNV.NifProbe <archive.bsa> <logical-nif-or-kf-path> | " +
        "--directory <path> | --cell-audit <mod-stack.json> <plugin:object-id> | " +
        "--shader-contract <mod-stack.json> | " +
        "--skin-contract <archive.bsa> <logical-nif-path> | --bsx-contract <mod-stack.json> | " +
        "--bsx-audit <mod-stack.json> | --bsx-synthetic");
    return 2;
}

try
{
    var archive = new FalloutBsaArchive(args[0]);
    var payload = archive.Read(args[1]);
    var result = Probe(payload);
    var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    Console.WriteLine(
        $"OPENNV_NIF_OK sha256={sha256} bytes={payload.Length} {result}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"OPENNV_NIF_ERROR {error.GetType().Name}: {error.Message}");
    return 1;
}

static int AuditCell(string sourcePath, string cellText)
{
    var resolvedSource = Path.GetFullPath(sourcePath);
    var configured = false;
    FalloutPluginStack stack;
    Func<string, byte[]?> read;
    if (Directory.Exists(resolvedSource))
    {
        stack = FalloutPluginStack.Load(resolvedSource, ["FalloutNV.esm"]);
        var meshes = new FalloutBsaArchive(Path.Combine(resolvedSource, "Fallout - Meshes.bsa"));
        read = path => meshes.Contains(path) ? meshes.Read(path) : null;
    }
    else
    {
        var bytes = File.ReadAllBytes(resolvedSource);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var dataRoot = root.GetProperty("roots")[0].GetProperty("root").GetString()!;
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        RuntimeOwnedContentSource.Configure(
            dataRoot, resolvedSource, hash, root.GetProperty("stackId").GetString());
        configured = true;
        var source = RuntimeOwnedContentSource.Current!;
        stack = FalloutPluginStack.Load(source.PluginSources);
        read = path => source.TryRead(path, null, out var payload, out _) ? payload : null;
    }
    try
    {
        var separator = cellText.LastIndexOf(':');
        if (separator <= 0 || !uint.TryParse(
                cellText[(separator + 1)..],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var objectId))
            throw new ArgumentException("CELL key must be plugin:hex-object-id.", nameof(cellText));
        using (stack)
        {
            var cell = FalloutCellSceneReader.Read(
                stack, new FalloutFormKey(cellText[..separator], objectId));
            var paths = cell.BaseObjects.Values.Select(value => value.ModelPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var failures = new List<string>();
            var types = new Dictionary<string, int>(StringComparer.Ordinal);
            var collisionSizes = new Dictionary<string, int>(StringComparer.Ordinal);
            var shaders = new Dictionary<string, int>(StringComparer.Ordinal);
            var parsed = 0;
            foreach (var path in paths)
            {
                try
                {
                    var payload = read(path);
                    if (payload is null)
                        throw new FileNotFoundException("Winning model is missing.");
                    var nif = FalloutNifFile.Read(payload);
                    foreach (var block in nif.Blocks)
                    {
                        types[block.TypeName] = types.GetValueOrDefault(block.TypeName) + 1;
                        if (block.TypeName.StartsWith("bhk", StringComparison.Ordinal) ||
                            block.TypeName.StartsWith("hk", StringComparison.Ordinal))
                        {
                            var sizeKey = $"{block.TypeName}:{block.Size}";
                            collisionSizes[sizeKey] = collisionSizes.GetValueOrDefault(sizeKey) + 1;
                        }
                        switch (block.TypeName)
                        {
                            case "NiNode":
                            case "NiBone":
                            case "BSFadeNode":
                                _ = nif.ReadNode(block.Index);
                                break;
                            case "NiTriShape":
                            case "NiTriStrips":
                                _ = nif.ReadGeometry(block.Index);
                                break;
                            case "NiTriShapeData":
                            case "NiTriStripsData":
                                _ = nif.ReadMeshData(block.Index);
                                break;
                            case "BSShaderPPLightingProperty":
                                var pp = (FalloutNifShaderProperty)nif.ReadObject(block.Index);
                                var ppKey = $"pp:{pp.ShaderType}:{pp.ShaderFlags:x8}:{pp.ShaderFlags2:x8}";
                                shaders[ppKey] = shaders.GetValueOrDefault(ppKey) + 1;
                                break;
                            case "BSShaderNoLightingProperty":
                                var unlit = (FalloutNifNoLightingProperty)nif.ReadObject(block.Index);
                                var unlitKey =
                                    $"unlit:{unlit.ShaderType}:{unlit.ShaderFlags:x8}:{unlit.ShaderFlags2:x8}";
                                shaders[unlitKey] = shaders.GetValueOrDefault(unlitKey) + 1;
                                break;
                            case "BSShaderTextureSet":
                            case "NiMaterialProperty":
                            case "NiAlphaProperty":
                            case "BSXFlags":
                            case "NiStringExtraData":
                            case "BSBound":
                            case "BSFurnitureMarker":
                            case "NiControllerManager":
                            case "NiMultiTargetTransformController":
                            case "NiTextKeyExtraData":
                            case "NiDefaultAVObjectPalette":
                            case "bhkCollisionObject":
                            case "bhkRigidBody":
                            case "bhkRigidBodyT":
                            case "bhkMoppBvTreeShape":
                            case "bhkPackedNiTriStripsShape":
                            case "hkPackedNiTriStripsData":
                            case "bhkBoxShape":
                            case "bhkSphereShape":
                            case "bhkCapsuleShape":
                            case "bhkConvexVerticesShape":
                            case "bhkListShape":
                            case "bhkConvexTransformShape":
                                _ = nif.ReadObject(block.Index);
                                break;
                        }
                    }
                    parsed++;
                }
                catch (Exception error)
                {
                    failures.Add($"{path} => {error.GetType().Name}: {error.Message}");
                }
            }
            Console.WriteLine($"OPENNV_NIF_CELL_AUDIT models={paths.Length} parsed={parsed} failures={failures.Count}");
            Console.WriteLine("OPENNV_NIF_CELL_TYPES " + string.Join(',',
                types.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
            Console.WriteLine("OPENNV_NIF_CELL_SHADERS " + string.Join(',',
                shaders.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
            Console.WriteLine("OPENNV_NIF_CELL_COLLISION_SIZES " + string.Join(',',
                collisionSizes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
            foreach (var failure in failures)
                Console.WriteLine($"OPENNV_NIF_CELL_FAILURE {failure}");
            return failures.Count == 0 ? 0 : 1;
        }
    }
    finally
    {
        if (configured)
            RuntimeOwnedContentSource.Clear();
    }
}

static string Probe(byte[] payload)
{
    var file = FalloutNifFile.Read(payload);
    var nodes = 0;
    var geometry = 0;
    var meshData = 0;
    var vertices = 0;
    var triangles = 0;
    var sequences = 0;
    var controlledBlocks = 0;
    var interpolators = 0;
    var transformData = 0;
    var transformKeys = 0;
    var shaders = new List<string>();
    var textureSets = new List<string>();
    var boneLodControllers = new List<string>();
    var directTransformControllers = new List<string>();
    var textKeyExtraData = new List<string>();
    var geometryContracts = new List<string>();
    var materialContracts = new List<string>();
    foreach (var block in file.Blocks)
    {
        switch (block.TypeName)
        {
            case "NiNode":
            case "NiBone":
            case "BSFadeNode":
                _ = file.ReadNode(block.Index);
                nodes++;
                break;
            case "NiTriShape":
            case "NiTriStrips":
                var geometryValue = file.ReadGeometry(block.Index);
                var geometryMesh = file.ReadMeshData(geometryValue.Data);
                geometryContracts.Add($"{block.Index}:{geometryValue.Name}:" +
                    $"flags=0x{geometryValue.Flags:x4}:properties=" +
                    $"[{string.Join(',', geometryValue.Properties.Select(reference => reference == -1
                        ? "-1"
                        : $"{reference}:{file.Blocks[reference].TypeName}"))}]:" +
                    $"data={geometryValue.Data}:colors={geometryMesh.Colors.Length}:" +
                    $"skin={geometryValue.SkinInstance}:" +
                    $"collision={geometryValue.CollisionObject}");
                geometry++;
                break;
            case "NiTriShapeData":
            case "NiTriStripsData":
                var mesh = file.ReadMeshData(block.Index);
                meshData++;
                vertices += mesh.Vertices.Length;
                triangles += mesh.Triangles.Length;
                break;
            case "NiControllerSequence":
                var sequence = file.ReadControllerSequence(block.Index);
                sequences++;
                controlledBlocks += sequence.ControlledBlocks.Length;
                break;
            case "NiTransformInterpolator":
                _ = file.ReadObject(block.Index);
                interpolators++;
                break;
            case "NiTransformData":
                var data = (FalloutNifTransformData)file.ReadObject(block.Index);
                transformData++;
                transformKeys += data.QuaternionRotations.Length +
                    data.XyzRotations.Sum(axis => axis.Length) + data.Translations.Length + data.Scales.Length;
                break;
            case "NiBSBoneLODController":
                var boneLod = (FalloutNifBoneLodController)file.ReadObject(block.Index);
                boneLodControllers.Add($"{block.Index}:lod={boneLod.Lod}/lods={boneLod.LodCount}/" +
                    $"groups={boneLod.DeclaredNodeGroupCount}/" +
                    $"sizes={string.Join('+', boneLod.NodeGroups.Select(group => group.Length))}/" +
                    $"{FormatTime(boneLod.Time)}");
                break;
            case "NiTransformController":
                var direct = (FalloutNifTransformController)file.ReadObject(block.Index);
                var directChain = $"interpolator={direct.Interpolator}";
                if (direct.Interpolator != -1)
                {
                    if (file.ReadObject(direct.Interpolator) is not
                            FalloutNifTransformInterpolator directInterpolator)
                        throw new InvalidDataException(
                            $"Direct transform controller {block.Index} has an unsupported interpolator.");
                    directChain += $"/translation={directInterpolator.Translation}/" +
                        $"rotation={directInterpolator.Rotation}/scale={directInterpolator.Scale:R}/" +
                        $"data={directInterpolator.Data}";
                    if (directInterpolator.Data != -1)
                    {
                        if (file.ReadObject(directInterpolator.Data) is not FalloutNifTransformData directData)
                            throw new InvalidDataException(
                                $"Direct transform controller {block.Index} has unsupported transform data.");
                        directChain += $"/rotationType={directData.RotationType}/" +
                            $"quaternionKeys={directData.QuaternionRotations.Length}/" +
                            $"xyzKeys={directData.XyzRotations.Sum(axis => axis.Length)}/" +
                            $"translationKeys={directData.Translations.Length}/" +
                            $"scaleKeys={directData.Scales.Length}";
                    }
                }
                directTransformControllers.Add($"{block.Index}:{directChain}/" + FormatTime(direct.Time));
                break;
            case "NiTextKeyExtraData":
                var textKeys = (FalloutNifTextKeyExtraData)file.ReadObject(block.Index);
                textKeyExtraData.Add($"{block.Index}:name={textKeys.Name}/" +
                    string.Join('+', textKeys.Keys.Select(key => $"{key.Time:R}:{key.Value}")));
                break;
            case "BSShaderPPLightingProperty":
                var lighting = (FalloutNifShaderProperty)file.ReadObject(block.Index);
                shaders.Add($"{block.Index}:pp:{lighting.ShaderType}:" +
                    $"{lighting.ShaderFlags:x8}:{lighting.ShaderFlags2:x8}");
                break;
            case "BSShaderNoLightingProperty":
                var noLighting = (FalloutNifNoLightingProperty)file.ReadObject(block.Index);
                shaders.Add($"{block.Index}:unlit:{noLighting.ShaderType}:" +
                    $"{noLighting.ShaderFlags:x8}:{noLighting.ShaderFlags2:x8}");
                break;
            case "BSShaderTextureSet":
                var textureSet = (FalloutNifShaderTextureSet)file.ReadObject(block.Index);
                textureSets.Add($"{block.Index}:{string.Join('|', textureSet.Textures)}");
                break;
            case "NiMaterialProperty":
                var materialValue = (FalloutNifMaterialProperty)file.ReadObject(block.Index);
                materialContracts.Add($"{block.Index}:controller={materialValue.Controller}:" +
                    $"extra=[{string.Join(',', materialValue.ExtraData)}]:" +
                    $"specular={materialValue.Specular}:emissive={materialValue.Emissive}:" +
                    $"gloss={materialValue.Glossiness:R}:alpha={materialValue.Alpha:R}:" +
                    $"emissiveMultiple={materialValue.EmissiveMultiple:R}");
                break;
            case "BSXFlags":
            case "NiIntegerExtraData":
            case "NiStringExtraData":
            case "BSBound":
            case "BSFurnitureMarker":
            case "NiControllerManager":
            case "NiMultiTargetTransformController":
            case "NiDefaultAVObjectPalette":
            case "bhkCollisionObject":
            case "NiAlphaProperty":
                _ = file.ReadObject(block.Index);
                break;
        }
    }
    return $"version2={file.UserVersion2} blocks={file.Blocks.Count} roots={file.Roots.Count} " +
        $"nodes={nodes} geometry={geometry} meshData={meshData} vertices={vertices} " +
        $"triangles={triangles} sequences={sequences} controlled={controlledBlocks} " +
        $"interpolators={interpolators} transformData={transformData} transformKeys={transformKeys} " +
        $"shaders={string.Join(';', shaders)} " +
        $"textures={string.Join(';', textureSets)} " +
        $"boneLod={string.Join(';', boneLodControllers)} " +
        $"directTransforms={string.Join(';', directTransformControllers)} " +
        $"textKeys={string.Join(';', textKeyExtraData)} " +
        $"geometryContracts={string.Join(';', geometryContracts)} " +
        $"materialContracts={string.Join(';', materialContracts)} " +
        $"types={string.Join(',', file.Blocks.Select(block => block.TypeName).Distinct())}";
}

static void RequireRejectedBsx(uint flags, FalloutNifBsxEvidence evidence)
{
    try
    {
        FalloutNifBsxContract.Validate(flags, evidence);
        throw new InvalidDataException($"Unsupported BSX flags 0x{flags:x8} did not fail closed.");
    }
    catch (NotSupportedException)
    {
    }
}

static FalloutNifBsxEvidence ReadBsxEvidence(FalloutNifFile file, uint externalEmittanceFlag)
{
    var collisions = file.Blocks
        .Where(block => block.TypeName is "bhkCollisionObject" or "bhkBlendCollisionObject")
        .Select(block => (FalloutNifCollisionObject)file.ReadObject(block.Index))
        .ToArray();
    var hasConstrainedCollision = collisions.Any(collision =>
        collision.Body != -1 &&
        file.ReadObject(collision.Body) is FalloutNifRigidBody body &&
        body.Constraints.Length != 0);
    var hasEditorMarker = file.Blocks
        .Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
        .Select(block => file.ReadNode(block.Index))
        .Any(node => node.Name.Equals("EditorMarker", StringComparison.Ordinal));
    var hasExternalEmittance = file.Blocks
        .Where(block => block.TypeName is "BSShaderPPLightingProperty" or
            "BSShaderNoLightingProperty")
        .Select(block => file.ReadObject(block.Index))
        .Any(shader => shader switch
        {
            FalloutNifShaderProperty lighting =>
                (lighting.ShaderFlags & externalEmittanceFlag) != 0,
            FalloutNifNoLightingProperty unlit =>
                (unlit.ShaderFlags & externalEmittanceFlag) != 0,
            _ => false,
        });
    return new FalloutNifBsxEvidence(
        collisions.Length != 0,
        collisions.Any(collision => collision.IsBlend),
        hasConstrainedCollision,
        hasEditorMarker,
        hasExternalEmittance);
}

static string FormatTime(FalloutNifTimeController value) =>
    $"next={value.NextController}/flags=0x{value.Flags:x4}/frequency={value.Frequency:R}/" +
    $"phase={value.Phase:R}/start={value.StartTime:R}/stop={value.StopTime:R}/target={value.Target}";

static string FormatPointInterpolator(FalloutNifFile file, int reference)
{
    if (file.ReadObject(reference) is not FalloutNifPoint3Interpolator interpolator)
        return $"{reference}:unsupported";
    var keys = interpolator.Data == -1
        ? ""
        : string.Join(';', ((FalloutNifPositionData)file.ReadObject(interpolator.Data)).Keys
            .Select(value => $"{value.Time:R}:{value.Value}"));
    return $"{reference}:value={interpolator.Value}:data={interpolator.Data}:keys={keys}";
}

static string FormatScalarKeys(IEnumerable<FalloutNifScalarKey> keys) => string.Join(';',
    keys.Select(key => $"{key.Time:R}:{key.Value:R}:i{key.Interpolation}"));

static string FormatVectorKeys(IEnumerable<FalloutNifVectorKey> keys) => string.Join(';',
    keys.Select(key => $"{key.Time:R}:{key.Value}:i{key.Interpolation}"));
