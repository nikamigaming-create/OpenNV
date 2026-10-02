using System.Reflection;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativeTagSkillMenuAudit
{
    private async Task VerifyDriver(FalloutPluginStack records, FalloutNativeTagSkillContract contract)
    {
        var previousPause = GetTree().Paused; var previousMouseMode = Input.MouseMode;
        var player = new RuntimeNativePlayer();
        NativeTagOpeningDriverFixture? driver = null;
        try
        {
            var configuration = RuntimeConfiguration.Load();
            DesktopInputMap.Configure(configuration.Player.DesktopInput);
            player.Configure(configuration, Transform3D.Identity); AddChild(player);
            player.SetPhysicsProcess(false); player.SetProcess(false);
            var sourceControls = new FalloutPlayerControlState(true, false, false, false, true, true, false);
            player.ApplySourceControls(sourceControls);
            var actor = records.RuntimeFormKey(7);
            var race = FalloutDialogueTopic.RequiredForm(records.GetEffective(actor), "RNAM");
            var ownerType = typeof(RuntimeNativeOpeningStageDriver);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var synchronize = ownerType.GetMethod("SynchronizeTagSkillEntry", flags) ?? throw new MissingMethodException("Tag synchronize owner");
            foreach (var priorModal in new[] { false, true })
                foreach (var outcome in new[] { "cancel", "accept", "failure", "exit" })
                {
                    driver = new();
                    void Bind(string field, object value) => (ownerType.GetField(field, flags) ?? throw new MissingFieldException(field)).SetValue(driver, value);
                    var failValue = false;
                    var skills = new FalloutPlayerSkills(records,
                        () => failValue ? throw new NotSupportedException("Synthetic unsupported player SPECIAL.") : new(5, 5, 5, 5, 5, 5, 5),
                        _ => false, () => [], null, new(), actor, () => race, () => false);
                    Bind("_player", player); Bind("_pluginStack", records); Bind("_tagSkillContract", contract); Bind("_playerSkills", skills);
                    Bind("_quests", new FalloutQuestState(records)); Bind("_sourceQuestEditorId", "VCG01");
                    AddChild(driver); driver.SetProcess(false);
                    player.SetModalInput(priorModal);
                    var priorMouse = priorModal ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
                    Input.MouseMode = priorMouse; GetTree().Paused = priorModal;
                    synchronize.Invoke(driver, [true]);
                    var entry = driver.GetChildren().OfType<RuntimeNativeTagSkillEntry>().Single();
                    var released = 0; entry.Released += () => released++;
                    if (!player.ModalInput || !GetTree().Paused || Input.MouseMode != Input.MouseModeEnum.Visible ||
                        !driver.ActiveMenus().SequenceEqual(new[] { 1048u }))
                        throw new InvalidDataException("Driver tag entry did not acquire its input/pause owner.");
                    var menu = entry.GetChildren().OfType<NativeOwnedTagSkillMenu>().Single();
                    if (outcome == "failure")
                    {
                        failValue = true;
                        for (var frame = 0; frame < 3; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        if (driver.ExecutionError is null || menu.Error is null || !player.ModalInput || !GetTree().Paused)
                            throw new InvalidDataException("Driver tag failure lost its blocked input owner or telemetry.");
                    }
                    if (outcome == "accept")
                    {
                        foreach (var row in menu.GetChildren().OfType<NativeBitmapMenuButton>()
                            .Where(button => button.Name.ToString().StartsWith("Skill_", StringComparison.Ordinal)).Take(contract.RequiredCount))
                            row.EmitSignal(BaseButton.SignalName.Pressed);
                        foreach (var pressed in new[] { true, false })
                            GetViewport().PushInput(new InputEventKey { Keycode = Key.A, PhysicalKeycode = Key.A, Pressed = pressed }, true);
                        var accepted = (IReadOnlyList<FalloutNativeSkillIdentity>)(ownerType.GetField("_tagSkills", flags)!.GetValue(driver)!);
                        if (accepted.Count != contract.RequiredCount) throw new InvalidDataException("Driver tag acceptance lost the draft.");
                    }
                    else if (outcome == "exit") { driver.Free(); driver = null; }
                    else synchronize.Invoke(driver, [false]);
                    if (GodotObject.IsInstanceValid(entry)) entry.ReleasePause();
                    for (var frame = 0; frame < 2; ++frame) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (player.ModalInput != priorModal || Input.MouseMode != priorMouse || GetTree().Paused != priorModal ||
                        player.SourceControls != sourceControls || released != 1 || (driver is not null && driver.ActiveMenus().Any()))
                        throw new InvalidDataException($"Driver tag {outcome} did not restore prior modal/mouse/pause exactly once with the source control mask intact.");
                    driver?.Free(); driver = null;
                }
        }
        finally
        {
            driver?.Free(); player.Free(); GetTree().Paused = previousPause; Input.MouseMode = previousMouseMode;
        }
    }
}

// This fixture suppresses campaign startup only; sync, acceptance and retirement
// execute the inherited production methods with isolated owned menu/player data.
internal partial class NativeTagOpeningDriverFixture : RuntimeNativeOpeningStageDriver
{
    public override void _Ready() { }
}
