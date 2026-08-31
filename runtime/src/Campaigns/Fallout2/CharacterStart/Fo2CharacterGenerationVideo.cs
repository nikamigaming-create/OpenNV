using Godot;


namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2CharacterGenerationVideo
{
    private const int ShortHoldFrames = 40;
    private const int ViewHoldFrames = 55;
    private const int GameplayHoldFrames = 120;
    private const int GroundingFrames = 120;

    internal static async Task Run(Fo2CharacterStartHost host, string character)
    {
        try
        {
            if (host.RestoredFromSave || host.Runtime is not null)
                throw new InvalidOperationException(
                    "Fallout 2 character video requires a fresh character-start save.");
            await WaitForDraws(host, ShortHoldFrames);
            var premadeIndex = character switch
            {
                "narg" => 0,
                "mingan" => 1,
                "chitsa" => 2,
                "custom-male" or "custom-female" => -1,
                _ => throw new ArgumentException(
                    $"Unsupported Fallout 2 character-video identity: {character}"),
            };
            if (premadeIndex >= 0)
            {
                host.Picker.Select(premadeIndex);
                await WaitForDraws(host, ViewHoldFrames);
                host.Picker.TogglePortraitMode();
                await WaitForDraws(host, ViewHoldFrames);
                host.Picker.ChooseCurrent();
                await HoldGameplay(host, character);
                return;
            }

            var female = character == "custom-female";
            host.Picker.Select(female ? 2 : 0);
            await WaitForDraws(host, ShortHoldFrames);
            host.Picker.FocusCreateCharacterControl();
            await WaitForDraws(host, ShortHoldFrames);
            var editor = host.Picker.PressCreateCharacterControl();
            editor.SetCharacterName(female ? "MARA" : "KORIN");
            editor.SetSex(female ? "Female" : "Male");
            editor.SetAge(female ? 18 : 26);
            editor.SetSpecial(female
                ? new[] { 4, 7, 5, 6, 8, 5, 5 }
                : new[] { 7, 6, 7, 4, 5, 6, 5 });
            editor.SetTaggedSkills(female
                ? new[] { "Sneak", "First Aid", "Speech" }
                : new[] { "Small Guns", "Melee Weapons", "Speech" });
            editor.SetTraits(female
                ? new[] { "Gifted", "Good Natured" }
                : new[] { "Fast Shot" });
            if (female)
            {
                editor.SetFaceShape(Fo2ProceduralPortrait.RoundFace);
                editor.SetHairStyle(Fo2ProceduralPortrait.LongHair);
                editor.SetSkinTone(Fo2ProceduralPortrait.DeepSkin);
                editor.SetHairColor(Fo2ProceduralPortrait.BlackHairColor);
                editor.SetEyeColor(Fo2ProceduralPortrait.GreenEyeColor);
                editor.SetBrowStyle(Fo2ProceduralPortrait.ArchedBrow);
                editor.SetNoseStyle(Fo2ProceduralPortrait.NarrowNose);
                editor.SetMouthStyle(Fo2ProceduralPortrait.SmallMouth);
            }
            else
            {
                editor.SetFaceShape(Fo2ProceduralPortrait.AngularFace);
                editor.SetHairStyle(Fo2ProceduralPortrait.SweptHair);
                editor.SetSkinTone(Fo2ProceduralPortrait.LightSkin);
                editor.SetHairColor(Fo2ProceduralPortrait.AuburnHairColor);
                editor.SetEyeColor(Fo2ProceduralPortrait.BlueEyeColor);
                editor.SetBrowStyle(Fo2ProceduralPortrait.HeavyBrow);
                editor.SetNoseStyle(Fo2ProceduralPortrait.BroadNose);
                editor.SetMouthStyle(Fo2ProceduralPortrait.WideMouth);
            }

            editor.ActivateRulesControl();
            await WaitForDraws(host, ViewHoldFrames);
            editor.ActivateReflectronFaceControl();
            await WaitForDraws(host, ViewHoldFrames);
            editor.ActivateReflectronBodyControl();
            await WaitForDraws(host, ViewHoldFrames);
            editor.ToggleClassicProjection();
            await WaitForDraws(host, ViewHoldFrames);
            editor.ActivateReflectronFaceControl();
            await WaitForDraws(host, ViewHoldFrames);
            editor.ToggleClassicProjection();
            editor.ActivateReflectronBodyControl();
            if (!editor.CanConfirm)
                throw new InvalidOperationException(
                    "Fallout 2 custom character is incomplete before acceptance.");
            editor.Confirm();
            await HoldGameplay(host, character);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CHARACTER_VIDEO_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task HoldGameplay(Fo2CharacterStartHost host, string character)
    {
        var runtime = host.Runtime ?? throw new InvalidOperationException(
            "Fallout 2 character did not enter Arroyo.");
        if (host.OpeningHandoffTask is not null)
        {
            host.OpeningHandoff?.RequestSkip();
            await host.OpeningHandoffTask;
            if (host.OpeningHandoff is not { Completed: true, ControlReleased: true })
                throw new InvalidOperationException(
                    "Fallout 2 character video did not complete the opening-to-Arroyo handoff.");
        }
        for (var frame = 0; frame < GroundingFrames && !runtime.Player.IsOnFloor(); frame++)
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        await WaitForDraws(host, GameplayHoldFrames);
        GD.Print($"OPENNV_FO2_CHARACTER_VIDEO_COMPLETE character={character}");
        host.GetTree().Quit(0);
    }

    private static async Task WaitForDraws(Node host, int count)
    {
        for (var frame = 0; frame < count; frame++)
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
