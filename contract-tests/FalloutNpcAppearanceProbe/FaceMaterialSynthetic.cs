using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class FaceMaterialSynthetic
{
    internal static void Run(FalloutPluginStack stack, FalloutNpcAppearance appearance)
    {
        var detail = FalloutNpcFaceMaterial.DefaultDetailMod();
        Require(detail.Width == 32 && detail.Height == 32 && detail.LogicalPath is null &&
            Enumerable.Range(0, 1024).All(i => detail.Rgba8.AsSpan(i * 4, 4).SequenceEqual(new byte[] { 62, 65, 62, 64 })),
            "native detail default bytes and extent");
        Require(FalloutNpcFaceMaterial.DefaultBaseMod().Rgba8.All(value => value == 128), "native base default");
        var settings = FalloutNpcFaceMaterial.ParseSettings(
            ["[Other]", "bFaceGenTexturing=0", "[General]", "bFaceGenTexturing=1", "bLoadFaceGenHeadEGTFiles=0 ; observed mode"], "fixture.ini");
        Require(settings.FaceGenTexturing && !settings.LoadHeadEgtFiles, "source INI section and boolean semantics");
        Throws(() => FalloutNpcFaceMaterial.ParseSettings(["[General]", "bFaceGenTexturing=guess"], "fixture.ini"));
        var npc = new FalloutFormKey("fixture.esm", 0x812);
        var race = new FalloutFormKey("fixture.esm", 0x900);
        Require(FalloutNpcFaceMaterial.PreprocessedBaseModPath(npc, "fixture.esm", false, false, race) ==
            "textures/characters/facemods/fixture.esm/00000812_0.dds", "source identity path");
        Require(FalloutNpcFaceMaterial.PreprocessedBaseModPath(npc, "fixture.esm", true, true, race) ==
            "textures/characters/facemods/fixture.esm/F00000900_00000812_0.dds", "all-races sex/race path");
        Require(FalloutNpcFaceMaterial.PreprocessedBaseModPath(npc, "Update.esm", false, false, race).Contains("/FalloutNV.esm/"),
            "source update-master folder rule");
        Throws(() => FalloutNpcFaceMaterial.PreprocessedBaseModPath(npc, "../escape.esm", false, false, race));

        var dds = new byte[128];
        "DDS "u8.CopyTo(dds);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(12), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(16), 128);
        var head = appearance.Models.Single(part => part.Role == "head") with { TexturePath = "textures/head-old.dds" };
        var resolved = FalloutNpcFaceMaterial.Resolve(stack, appearance, head, "textures/head.dds", "textures/head_n.dds",
            "textures/head_sk.dds", settings, _ => dds);
        Require(resolved.CanRender && resolved.BaseTexturePath == "textures/head-old.dds" &&
            resolved.NormalTexturePath == "textures/head-old_n.dds" && resolved.ScatteringTexturePath == "textures/head-old_sk.dds" &&
            resolved.BaseMod.LogicalPath == "textures/characters/facemods/base.esm/00000100_0.dds" &&
            resolved.BaseMod.Width == 128 && resolved.BaseMod.Height == 64, "head overrides and owned DDS binding");
        var absent = FalloutNpcFaceMaterial.Resolve(stack, appearance, head, "textures/head.dds", "textures/head_n.dds",
            null, settings, path => path.Contains("facemods/") ? null : dds);
        Require(!absent.CanRender && absent.Blockers.Any(value => value.StartsWith("facegen-base-mod-resource-absent:")),
            "missing face DDS remains visible");
        var eye = head with { Role = "eye-left", TexturePath = "textures/blue-eye.dds" };
        var eyeInputs = FalloutNpcFaceMaterial.Resolve(stack, appearance, eye, "textures/eye.dds", "textures/shared/flat_n.dds",
            null, settings, _ => dds);
        Require(eyeInputs.BaseTexturePath == "textures/blue-eye.dds" && eyeInputs.NormalTexturePath == "textures/shared/flat_n.dds",
            "eye diffuse override retains source normal");
        Console.WriteLine("OPENNV_FACEGEN_MATERIAL_INPUTS_OK nativeDefaults=true ownedDds=true sourceIni=true textureIdentity=true missingResourceVisible=true");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid FaceGen material input was accepted.");
    }
}
