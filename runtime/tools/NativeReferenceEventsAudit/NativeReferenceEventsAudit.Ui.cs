using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeReferenceEventsAudit
{
    private async Task UiClock()
    {
        var ui = FalloutUiComponentStore.Synthetic(XElement.Parse("<menu name='StartMenu'><rect name='Animator'>" +
            "<value>0</value><mirror><copy src='me()' trait='value'/></mirror></rect></menu>"));
        const string path = "StartMenu/Animator/value";
        var clock = new RuntimeNativeUiClock(() => ui);
        var scale = Engine.TimeScale;
        try
        {
            AddChild(clock);
            ui.SetFloatGradual(path, 0, 30, 30);
            async Task Frames()
            {
                for (var frame = 0; frame < 5; ++frame) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Frames();
            var before = ui.GetFloat(path);
            Require(before > 0, "Native UI animation has no elapsed-time owner.");
            GetTree().Paused = true;
            Engine.TimeScale = 0;
            var ticks = Time.GetTicksUsec();
            await Frames();
            var elapsed = (Time.GetTicksUsec() - ticks) / 1_000_000d;
            var value = ui.GetFloat(path);
            Require(value > before && Math.Abs(value - before - elapsed) < 0.01 &&
                ui.GetFloat("StartMenu/Animator/mirror") == value,
                "Paused/scaled gameplay stopped the UI clock or its source dependencies.");
            clock.Free();
            before = ui.GetFloat(path);
            await Frames();
            Require(ui.GetFloat(path) == before, "Retired native UI clock continued changing the menu.");
            GD.Print("OPENNV_NATIVE_UI_CLOCK_PASS monotonic=true paused=true timeMult=zero sourceDependencies=live retired=false recording=false pixels=unverified fixture=true");
        }
        finally
        {
            Engine.TimeScale = scale;
            GetTree().Paused = false;
            if (GodotObject.IsInstanceValid(clock)) clock.Free();
        }
    }
}
