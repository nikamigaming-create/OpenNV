using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2ProceduralPortraitProof
{
    private const int SourceIndex = 2;
    private const int ExpectedAge = 20;
    private const int ExpectedSpecialTotal = 40;
    private const int ExpectedLongHairVisibleParts = 4;
    private const int HashLength = 64;
    private const int SpecialFour = 4;
    private const int SpecialFive = 5;
    private const int SpecialSix = 6;
    private const int SpecialSeven = 7;
    private const int SpecialEight = 8;
    private const string ExpectedName = "Asha";
    private const string ExpectedSex = "Female";

    internal static void RunWrite(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            var output = PrepareOutput(proofRoot, false);
            if (host.RestoredFromSave || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 portrait write proof requires an empty save boundary.");
            host.Picker.Select(SourceIndex);
            var editor = host.Picker.OpenCustom(false);
            editor.SetCharacterName(ExpectedName);
            editor.SetSex(ExpectedSex);
            editor.SetAge(ExpectedAge);
            editor.SetFaceShape(Fo2ProceduralPortrait.AngularFace);
            editor.SetHairStyle(Fo2ProceduralPortrait.LongHair);
            editor.SetSkinTone(Fo2ProceduralPortrait.DeepSkin);
            editor.TogglePreviewMode();
            editor.SetSpecial(
            [
                SpecialFour,
                SpecialSeven,
                SpecialFive,
                SpecialSix,
                SpecialEight,
                SpecialFive,
                SpecialFive,
            ]);
            if (!editor.CanConfirm || editor.AllocatedSpecial != ExpectedSpecialTotal)
                throw new InvalidOperationException(
                    "Fallout 2 portrait proof custom state is invalid.");
            var liveHeadMatches = editor.Live3DVisible &&
                editor.HeadPreview.FaceShapeId == Fo2ProceduralPortrait.AngularFace &&
                editor.HeadPreview.HairStyleId == Fo2ProceduralPortrait.LongHair &&
                editor.HeadPreview.SkinToneId == Fo2ProceduralPortrait.DeepSkin &&
                editor.HeadPreview.RecipeSha256 ==
                    Fo2ProceduralAppearanceCatalog.Load().Sha256 &&
                editor.HeadPreview.VisibleGeometryParts == ExpectedLongHairVisibleParts;
            editor.Confirm();
            var saved = host.PersistCurrentState();
            var appearance = saved.Character.Appearance;
            var repeat = Fo2ProceduralPortrait.Commit(
                saved.Character.Source,
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId);
            var selectedPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId));
            var alternateFacePixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                Fo2ProceduralPortrait.RoundFace,
                appearance.HairStyleId,
                appearance.SkinToneId));
            var alternateHairPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                Fo2ProceduralPortrait.CroppedHair,
                appearance.SkinToneId));
            var alternateSkinPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                Fo2ProceduralPortrait.LightSkin));
            var passed = Matches(saved.Character) &&
                liveHeadMatches &&
                appearance == repeat &&
                selectedPixels != alternateFacePixels &&
                selectedPixels != alternateHairPixels &&
                selectedPixels != alternateSkinPixels &&
                File.Exists(appearance.GeneratedPortraitPath) &&
                saved.Sha256.Length == HashLength;
            WriteReport(
                output,
                "fo2-custom-portrait-write-proof.json",
                new
                {
                    schema = "opennv-fo2-custom-portrait-write-proof/v1",
                    status = passed
                        ? "pass-generated-portrait-atomic-save"
                        : "fail-generated-portrait-write",
                    appearance,
                    save = new { path = saved.Path, sha256 = saved.Sha256 },
                    deterministicRepeat = appearance == repeat,
                    distinctFaceShapePixels = selectedPixels != alternateFacePixels,
                    distinctHairStylePixels = selectedPixels != alternateHairPixels,
                    distinctSkinTonePixels = selectedPixels != alternateSkinPixels,
                    matchingLive3dHead = liveHeadMatches,
                    mediaCaptureCreated = false,
                });
            GD.Print(passed
                ? $"OPENNV_FO2_CUSTOM_PORTRAIT_WRITE_PASS sha256={appearance.GeneratedPortraitSha256}"
                : $"OPENNV_FO2_CUSTOM_PORTRAIT_WRITE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CUSTOM_PORTRAIT_WRITE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    internal static void RunRestore(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            var output = PrepareOutput(proofRoot, true);
            var saved = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 portrait restore proof has no validated save.");
            var appearance = saved.Character.Appearance;
            var passed = host.RestoredFromSave && host.Runtime is not null &&
                Matches(saved.Character) &&
                File.Exists(appearance.GeneratedPortraitPath) &&
                saved.Sha256.Length == HashLength;
            WriteReport(
                output,
                "fo2-custom-portrait-restore-proof.json",
                new
                {
                    schema = "opennv-fo2-custom-portrait-restore-proof/v1",
                    status = passed
                        ? "pass-generated-portrait-cold-restore"
                        : "fail-generated-portrait-restore",
                    appearance,
                    save = new { path = saved.Path, sha256 = saved.Sha256 },
                    coldProcess = true,
                    mediaCaptureCreated = false,
                });
            GD.Print(passed
                ? $"OPENNV_FO2_CUSTOM_PORTRAIT_RESTORE_PASS sha256={appearance.GeneratedPortraitSha256}"
                : $"OPENNV_FO2_CUSTOM_PORTRAIT_RESTORE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CUSTOM_PORTRAIT_RESTORE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    private static bool Matches(Fo2CharacterSelection character)
    {
        var appearance = character.Appearance;
        return character.Mode == Fo2CharacterSelection.CreateMode &&
            character.Source.Id == "diplomat" &&
            character.Profile.Name == ExpectedName &&
            character.Profile.Sex == ExpectedSex &&
            character.Profile.Age == ExpectedAge &&
            appearance.Schema == Fo2CharacterAppearanceContract.ExpectedSchema &&
            appearance.FaceShapeId == Fo2ProceduralPortrait.AngularFace &&
            appearance.HairStyleId == Fo2ProceduralPortrait.LongHair &&
            appearance.SkinToneId == Fo2ProceduralPortrait.DeepSkin &&
            appearance.PortraitGeneratorId == Fo2ProceduralPortrait.GeneratorId &&
            appearance.AppearanceRecipeId == Fo2ProceduralAppearanceCatalog.ExpectedId &&
            appearance.AppearanceRecipeSha256 == Fo2ProceduralAppearanceCatalog.Load().Sha256 &&
            appearance.CustomFaceEdited && appearance.CustomPortraitGenerated &&
            appearance.GeneratedPortraitWidth == Fo2ProceduralPortrait.Width &&
            appearance.GeneratedPortraitHeight == Fo2ProceduralPortrait.Height;
    }

    private static string PrepareOutput(string proofRoot, bool requireExisting)
    {
        var output = Path.GetFullPath(proofRoot);
        if (File.Exists(output) || requireExisting != Directory.Exists(output))
            throw new InvalidOperationException(
                requireExisting
                    ? $"Fallout 2 portrait proof output is unavailable: {output}"
                    : $"Refusing to overwrite Fallout 2 portrait proof: {output}");
        if (!requireExisting)
            Directory.CreateDirectory(output);
        return output;
    }

    private static void WriteReport(string output, string filename, object report) =>
        File.WriteAllText(
            Path.Combine(output, filename),
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);

    private static string PixelSha256(Image image) => Convert.ToHexString(
        SHA256.HashData(image.GetData())).ToLowerInvariant();
}
