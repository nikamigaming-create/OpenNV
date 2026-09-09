using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

// Disposable source-state/menu integration test. It does not advance an ordinary
// save and does not stand in for speaker audio, seated camera or matched pixels.
public partial class NativeDialogueMenuAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 1) throw new ArgumentException("Dialogue menu audit needs one owned root.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var scene = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey);
            world.LoadCell(scene);
            var quests = new FalloutQuestState(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", "VCG01");
            var speaker = FalloutDialogueTopic.Find(records, "ACHR", "DocMitchellREF");
            var npc = records.GetEffective(FalloutDialogueTopic.RequiredForm(speaker, "NAME"));
            var slots = FalloutScriptLocals.Read(FalloutScriptLocals.AttachedScript(records, quest)!);
            quests.EnterStage(quest.FormKey, 80); quests.ApplyObjective(quest.FormKey, 40, true, true);
            quests.SetVariable(quest.FormKey, slots["bGiveTest"], 1);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                if (effect.Kind == FalloutReferenceEffectKind.SetStage) quests.EnterStage(effect.Target!.Value, effect.Stage);
                else if (effect.Kind is not (FalloutReferenceEffectKind.Texture or FalloutReferenceEffectKind.ReferenceEnable))
                    throw new NotSupportedException($"Menu audit effect {effect.Kind} is outside this test.");
            }));
            var conditions = new FalloutDialogueConditions(records, quests, speaker.FormKey,
                FalloutNpcAppearanceResolver.Resolve(records, npc.FormKey, speaker.FormKey));
            var conversation = new FalloutConversation(records, quests, conditions.Evaluate,
                (info, begin) => scripts.ExecuteResult(info, speaker.FormKey, begin));
            Exception? failure = null;
            var menu = new NativeOwnedDialogueMenu(conversation.CompleteResponse, error => failure ??= error);
            AddChild(menu);
            var name = FalloutDialogueTopic.Text(npc.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
            conversation.Start(npc.FormKey, FalloutDialogueTopic.Find(records, "DIAL", "GREETING").FormKey);
            var selections = 0; var responses = 0;
            while (conversation.Phase != "closed" && selections < 64 && responses < 256)
            {
                menu.Show(name, conversation, conversation.Choose);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (failure is not null) throw failure;
                var buttons = menu.GetChildren().OfType<BaseButton>().Where(value => value.Visible).ToArray();
                if (buttons.Length != (conversation.Phase == "speaking" ? 1 : conversation.Choices.Count) ||
                    buttons.Any(button => button.Size.X <= 0 || button.Size.Y <= 0))
                    throw new InvalidOperationException("Source menu lost an input region or offered choice.");
                var button = buttons[0];
                if (conversation.Phase == "speaking") responses++; else selections++;
                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
            if (conversation.Phase != "closed" || selections != 14 || failure is not null)
                throw failure ?? new InvalidOperationException("Source menu did not submit the full questionnaire.");
            GD.Print($"OPENNV_NATIVE_DIALOGUE_MENU_AUDIT_PASS choices={selections} responses={responses} sourceXml=true sourceFonts=true buttonSignals=true recording=off ordinary-input-audio-pixels=unverified");
            menu.Free();
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
