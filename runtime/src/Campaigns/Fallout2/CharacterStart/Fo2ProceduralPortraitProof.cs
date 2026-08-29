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
            editor.SetHairColor(Fo2ProceduralPortrait.AuburnHairColor);
            editor.SetEyeColor(Fo2ProceduralPortrait.BlueEyeColor);
            editor.SetBrowStyle(Fo2ProceduralPortrait.ArchedBrow);
            editor.SetNoseStyle(Fo2ProceduralPortrait.NarrowNose);
            editor.SetMouthStyle(Fo2ProceduralPortrait.WideMouth);
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
                MatchesLiveHead(editor.HeadPreview);
            var selectedFeatureState = editor.HeadPreview.FeatureState;
            var alternateHead = new Fo2ProceduralHeadPreview();
            host.AddChild(alternateHead);
            alternateHead.SetIdentity(
                ExpectedSex,
                Fo2ProceduralPortrait.AngularFace,
                Fo2ProceduralPortrait.LongHair,
                Fo2ProceduralPortrait.DeepSkin,
                Fo2ProceduralPortrait.AuburnHairColor,
                Fo2ProceduralPortrait.BlueEyeColor,
                Fo2ProceduralPortrait.StraightBrow,
                Fo2ProceduralPortrait.StandardNose,
                Fo2ProceduralPortrait.NeutralMouth);
            var distinctLive3dFeatureGeometry =
                selectedFeatureState != alternateHead.FeatureState;
            alternateHead.QueueFree();
            editor.Confirm();
            var saved = host.PersistCurrentState();
            var appearance = saved.Character.Appearance;
            var repeat = Fo2ProceduralPortrait.Commit(
                saved.Character.Source,
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId,
                appearance.HairColorId,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                appearance.MouthStyleId);
            var selectedPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId,
                appearance.HairColorId,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                appearance.MouthStyleId));
            var alternateFacePixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                Fo2ProceduralPortrait.RoundFace,
                appearance.HairStyleId,
                appearance.SkinToneId,
                appearance.HairColorId,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                appearance.MouthStyleId));
            var alternateHairPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                Fo2ProceduralPortrait.CroppedHair,
                appearance.SkinToneId,
                appearance.HairColorId,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                appearance.MouthStyleId));
            var alternateSkinPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                Fo2ProceduralPortrait.LightSkin,
                appearance.HairColorId,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                appearance.MouthStyleId));
            var alternateHairColorPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId,
                Fo2ProceduralPortrait.BlackHairColor,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                appearance.MouthStyleId));
            var alternateEyeColorPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId,
                appearance.HairColorId,
                Fo2ProceduralPortrait.GreenEyeColor,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                appearance.MouthStyleId));
            var alternateBrowPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId,
                appearance.HairColorId,
                appearance.EyeColorId,
                Fo2ProceduralPortrait.StraightBrow,
                appearance.NoseStyleId,
                appearance.MouthStyleId));
            var alternateNosePixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId,
                appearance.HairColorId,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                Fo2ProceduralPortrait.StandardNose,
                appearance.MouthStyleId));
            var alternateMouthPixels = PixelSha256(Fo2ProceduralPortrait.Render(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId,
                appearance.HairColorId,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                Fo2ProceduralPortrait.NeutralMouth));
            var passed = Matches(saved.Character) &&
                liveHeadMatches &&
                appearance == repeat &&
                selectedPixels != alternateFacePixels &&
                selectedPixels != alternateHairPixels &&
                selectedPixels != alternateSkinPixels &&
                selectedPixels != alternateHairColorPixels &&
                selectedPixels != alternateEyeColorPixels &&
                selectedPixels != alternateBrowPixels &&
                selectedPixels != alternateNosePixels &&
                selectedPixels != alternateMouthPixels &&
                distinctLive3dFeatureGeometry &&
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
                    distinctHairColorPixels = selectedPixels != alternateHairColorPixels,
                    distinctEyeColorPixels = selectedPixels != alternateEyeColorPixels,
                    distinctBrowPixels = selectedPixels != alternateBrowPixels,
                    distinctNosePixels = selectedPixels != alternateNosePixels,
                    distinctMouthPixels = selectedPixels != alternateMouthPixels,
                    distinctLive3dFeatureGeometry,
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
            var restoredHead = new Fo2ProceduralHeadPreview();
            host.AddChild(restoredHead);
            restoredHead.SetIdentity(
                saved.Character.Profile.Sex,
                appearance.FaceShapeId,
                appearance.HairStyleId,
                appearance.SkinToneId,
                appearance.HairColorId,
                appearance.EyeColorId,
                appearance.BrowStyleId,
                appearance.NoseStyleId,
                appearance.MouthStyleId);
            var liveHeadMatches = MatchesLiveHead(restoredHead);
            restoredHead.QueueFree();
            var passed = host.RestoredFromSave && host.Runtime is not null &&
                Matches(saved.Character) &&
                liveHeadMatches &&
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
                    matchingRestoredLive3dHead = liveHeadMatches,
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

    internal static void RunV8MigrationRestore(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            var output = PrepareOutput(proofRoot, false);
            var saved = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 v8 migration proof has no validated save.");
            using var document = JsonDocument.Parse(File.ReadAllBytes(saved.Path));
            var sourceSchema = document.RootElement.GetProperty("schema").GetString();
            var recipe = Fo2ProceduralAppearanceCatalog.Load();
            var appearance = saved.Character.Appearance;
            var passed = sourceSchema == Fo2CharacterStartSaveState.ColorAppearanceSchema &&
                host.RestoredFromSave && host.Runtime is not null &&
                saved.Character.Mode == Fo2CharacterSelection.CreateMode &&
                saved.Character.Profile.Name == ExpectedName &&
                appearance.FaceShapeId == Fo2ProceduralPortrait.AngularFace &&
                appearance.HairStyleId == Fo2ProceduralPortrait.LongHair &&
                appearance.SkinToneId == Fo2ProceduralPortrait.DeepSkin &&
                appearance.HairColorId == Fo2ProceduralPortrait.AuburnHairColor &&
                appearance.EyeColorId == Fo2ProceduralPortrait.BlueEyeColor &&
                appearance.BrowStyleId == recipe.DefaultBrowStyleId &&
                appearance.NoseStyleId == recipe.DefaultNoseStyleId &&
                appearance.MouthStyleId == recipe.DefaultMouthStyleId &&
                appearance.Schema == Fo2CharacterAppearanceContract.ExpectedSchema &&
                appearance.AppearanceRecipeId == Fo2ProceduralAppearanceCatalog.ExpectedId &&
                appearance.AppearanceRecipeSha256 == recipe.Sha256 &&
                File.Exists(appearance.GeneratedPortraitPath);
            WriteReport(
                output,
                "fo2-custom-portrait-v8-migration-proof.json",
                new
                {
                    schema = "opennv-fo2-custom-portrait-v8-migration-proof/v1",
                    status = passed
                        ? "pass-v8-feature-default-migration"
                        : "fail-v8-feature-default-migration",
                    sourceSchema,
                    appearance,
                    migratedDefaults = new
                    {
                        recipe.DefaultBrowStyleId,
                        recipe.DefaultNoseStyleId,
                        recipe.DefaultMouthStyleId,
                    },
                    coldProcess = true,
                    mediaCaptureCreated = false,
                });
            GD.Print(passed
                ? "OPENNV_FO2_CUSTOM_PORTRAIT_V8_MIGRATION_PASS"
                : $"OPENNV_FO2_CUSTOM_PORTRAIT_V8_MIGRATION_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CUSTOM_PORTRAIT_V8_MIGRATION_FAIL {exception}");
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
            appearance.HairColorId == Fo2ProceduralPortrait.AuburnHairColor &&
            appearance.EyeColorId == Fo2ProceduralPortrait.BlueEyeColor &&
            appearance.BrowStyleId == Fo2ProceduralPortrait.ArchedBrow &&
            appearance.NoseStyleId == Fo2ProceduralPortrait.NarrowNose &&
            appearance.MouthStyleId == Fo2ProceduralPortrait.WideMouth &&
            appearance.PortraitGeneratorId == Fo2ProceduralPortrait.GeneratorId &&
            appearance.AppearanceRecipeId == Fo2ProceduralAppearanceCatalog.ExpectedId &&
            appearance.AppearanceRecipeSha256 == Fo2ProceduralAppearanceCatalog.Load().Sha256 &&
            appearance.CustomFaceEdited && appearance.CustomPortraitGenerated &&
            appearance.GeneratedPortraitWidth == Fo2ProceduralPortrait.Width &&
            appearance.GeneratedPortraitHeight == Fo2ProceduralPortrait.Height;
    }

    private static bool MatchesLiveHead(Fo2ProceduralHeadPreview head)
    {
        var catalog = Fo2ProceduralAppearanceCatalog.Load();
        var brow = catalog.BrowStyle(Fo2ProceduralPortrait.ArchedBrow);
        var nose = catalog.NoseStyle(Fo2ProceduralPortrait.NarrowNose);
        var mouth = catalog.MouthStyle(Fo2ProceduralPortrait.WideMouth);
        var expectedFeatures = new Fo2LiveHeadFeatureState(
            brow.LiveY,
            brow.LiveRotationRadians,
            brow.LiveWidth,
            brow.LiveThickness,
            nose.HeadScale,
            mouth.LiveWidth,
            mouth.LiveHeight);
        return head.FaceShapeId == Fo2ProceduralPortrait.AngularFace &&
            head.HairStyleId == Fo2ProceduralPortrait.LongHair &&
            head.SkinToneId == Fo2ProceduralPortrait.DeepSkin &&
            head.HairColorId == Fo2ProceduralPortrait.AuburnHairColor &&
            head.EyeColorId == Fo2ProceduralPortrait.BlueEyeColor &&
            head.BrowStyleId == Fo2ProceduralPortrait.ArchedBrow &&
            head.NoseStyleId == Fo2ProceduralPortrait.NarrowNose &&
            head.MouthStyleId == Fo2ProceduralPortrait.WideMouth &&
            head.FeatureState == expectedFeatures &&
            head.HairAlbedo == catalog.HairColor(head.HairColorId).HeadAlbedo &&
            head.EyeAlbedo == catalog.EyeColor(head.EyeColorId).HeadAlbedo &&
            head.RecipeSha256 == catalog.Sha256 &&
            head.VisibleGeometryParts == ExpectedLongHairVisibleParts;
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
