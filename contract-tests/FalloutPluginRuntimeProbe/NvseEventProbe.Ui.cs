using System.Xml.Linq;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class NvseEventProbe
{
    private static void UiCommands(FalloutPluginStack records, FalloutPluginRecord quest, FalloutPluginRecord definition)
    {
        var ui = FalloutUiComponentStore.Synthetic(XElement.Parse("<menu name='StartMenu'><rect name='Animator'><value>0</value></rect></menu>"));
        using var world = new FalloutReferenceWorld(records, ui: ui);
        var quests = new FalloutQuestState(records);
        var executor = Executor(records, world, quests, new());
        const string path = "StartMenu/Animator/value";
        void Run(string body) => executor.ExecuteProgram(quest, definition,
            FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
        Run("SetUIFloatGradual \"" + path + "\" -0.025 0.025 0.15 2");
        Require(ui.GetFloat(path) == -0.025f, "Ordinary script command lost a signed endpoint or changed arity.");
        ui.AdvanceAnimations(0.075);
        Require(Math.Abs(ui.GetFloat(path) - 0.025f) < 0.000001f, "Ordinary script did not create a live repeating UI animation.");
        Run("SetUIFloatGradual \"" + path + "\"");
        var before = ui.GetFloat(path);
        ui.AdvanceAnimations(100);
        Require(ui.GetFloat(path) == before, "The source stop command left an active UI animation.");
        Reject(() => Run("SetUIFloatGradual \"" + path + "\" 1 2 1 0.5"));
        Require(ui.GetFloat(path) == before, "Invalid source mode mutated the UI value.");
        quests.SetVariable(quest.FormKey, 3, 2);
        var fallback = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
            defaultProcessingDelay: 0.01f, references: world);
        fallback.Advance(1);
        Require(quests.Variable(quest.FormKey, 3) == 3 && fallback.Capture().Instances.Single().Error is null &&
            ui.GetFloat(path) == -0.025f, "Fallback quest commands did not reach the same UI animation owner.");
        ui.AdvanceAnimations(0.075);
        Require(Math.Abs(ui.GetFloat(path) - 0.025f) < 0.000001f, "Fallback UI animation retained a different clock/state owner.");
        Console.WriteLine("OPENNV_UI_SCRIPT_ANIMATION_PASS reference=true fallbackQuest=true signedEndpoints=true repeated=true stop=true invalidAtomic=true");
    }
}
