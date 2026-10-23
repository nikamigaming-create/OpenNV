using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private static void ReputationDialogueAdapters(string mode = "all", ushort function = 573)
    {
        var directory = Directory.CreateTempSubdirectory("opennv-reputation-adapters-");
        var driver = new RuntimeNativeOpeningStageDriver();
        var player = new RuntimeNativePlayer();
        var speech = new RuntimeNativeSpeech();
        var conversation = new RuntimeNativeConversation();
        try
        {
            var bytes = new byte[28];
            BitConverter.GetBytes(function).CopyTo(bytes, 8);
            BitConverter.GetBytes(1u).CopyTo(bytes, 12); BitConverter.GetBytes(1u).CopyTo(bytes, 16);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Contact.esm"),
                Record("TES4", 0, Field("HEDR", new byte[12]))
                    .Concat(Record("REPU", 1, Field("DATA", BitConverter.GetBytes(100f))))
                    .Concat(Record("GLOB", 2, Field("EDID", Encoding.ASCII.GetBytes("FixtureGlobal\0")), Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(7f))))
                    .Concat(Record("INFO", 3, Field("CTDA", bytes))).ToArray());
            using var records = FalloutPluginStack.Load(directory.FullName, ["Contact.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
                defaultProcessingDelay: .1f, references: world);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(RuntimeNativeOpeningStageDriver);
            void Bind(string field, object value) => type.GetField(field, flags)!.SetValue(driver, value);
            var globals = new FalloutGlobalState(records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read));
            var inventory = new FalloutPlayerInventory();
            Bind("_scripts", scripts); Bind("_pluginStack", records); Bind("_quests", quests); Bind("_globals", globals);
            Bind("_inventory", inventory);
            Bind("_playerSkills", new FalloutPlayerSkills(records,
                () => throw new InvalidOperationException("Reputation query unexpectedly read SPECIAL."), _ => false, () => [],
                globals, inventory, records.RuntimeFormKey(0x14),
                () => throw new InvalidOperationException("Reputation query unexpectedly read race."), () => false));
            Func<FalloutCondition, float> Context(string name) =>
                (Func<FalloutCondition, float>)type.GetMethod(name, flags)!.Invoke(driver, null)!;
            // These are the production factories passed by the driver's actual
            // Configure calls. Read back the installed adapters, not a test world callback.
            speech.Configure(records, RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip,
                quest => quests.Stage(quest), Context("SpeechConditionContext"), quests: quests, references: world);
            conversation.Configure(records, quests, player, speech, Context("ConversationConditionContext"),
                (_, _, _) => throw new InvalidOperationException("Unexpected dialogue result in query audit."));
            var condition = FalloutCondition.Read(records.GetEffective(Key(3))).Single();
            var identity = new FalloutDialogueSpeaker(Key(0x20), Key(0x20), Key(0x21), "fixture", null, false);
            var adapters = new[]
            {
                (Name: "conversation", Adapter: (Func<FalloutCondition, float>)typeof(RuntimeNativeConversation)
                    .GetField("_runtimeConditions", flags)!.GetValue(conversation)!),
                (Name: "speech", Adapter: (Func<FalloutCondition, float>)typeof(RuntimeNativeSpeech)
                    .GetField("_conditionContext", flags)!.GetValue(speech)!)
            };
            var positive = function == 573 ? 9.25f : .0925f;
            var negative = function == 573 ? 4.5f : .045f;
            var legacy = function == 573 ? -3.5f : -.035f;
            foreach (var (name, adapter) in adapters.Where(value => mode == "all" || value.Name == mode))
            {
                world.RestoreReputations(new(true, []));
                var dialogue = new FalloutDialogueConditions(records, quests, Key(0x30), identity, adapter,
                    listener: Key(0x31), listenerIdentity: identity);
                Require(dialogue.Evaluate(condition) == 0, $"Actual {name} adapter lost fresh source zero.");
                world.SetReputation(Key(1), 1, 9.25); world.SetReputation(Key(1), 0, 4.5);
                foreach (var runOn in new uint[] { 0, 1, 2 })
                    Require(dialogue.Evaluate(condition with { RunOn = runOn }) == positive &&
                        dialogue.Evaluate(condition with { RunOn = runOn, Argument2 = 2 }) == negative,
                        $"Actual {name} adapter failed the shared query/selector through speaker/listener composition.");
                Require(adapter(condition with { Function = 74, Argument1 = 2 }) == 7,
                    $"Actual {name} adapter changed its existing global owner.");
                var before = JsonSerializer.Serialize(world.CaptureReputations());
                RejectReputationAdapter(() => adapter(condition with { Function = 575 }));
                RejectReputationAdapter(() => dialogue.Evaluate(condition with { RunOn = 3 }));
                RejectReputationAdapter(() => dialogue.Evaluate(condition with { RunOn = 2, Reference = 0xffff }));
                RejectReputationAdapter(() => dialogue.Evaluate(condition with { RunOn = 2, Reference = 2 }));
                Require(JsonSerializer.Serialize(world.CaptureReputations()) == before, "Adapter refusal changed reputation.");
                world.RestoreReputations(null);
                RejectReputationAdapter(() => dialogue.Evaluate(condition));
                world.SetReputation(Key(1), 1, -3.5);
                Require(dialogue.Evaluate(condition) == legacy, "Actual native adapter lost an explicit legacy axis.");
                RejectReputationAdapter(() => dialogue.Evaluate(condition with { Argument2 = 0 }));
                world.RestoreReputations(JsonSerializer.Deserialize<FalloutReputationsSnapshot>(before));
                Require(dialogue.Evaluate(condition) == positive, "Actual native adapter lost cold-restored values.");
                GD.Print($"OPENNV_NATIVE_REPUTATION_ADAPTER_PASS function={function} kind={name} productionFactory=true actualInstalledDelegate=true " +
                    "dialogueComposition=true nonPlayerListener=true rawSelector=true globalOwner=true cold=true legacyUnknown=true " +
                    "noMutation=true ordinaryConversationSpeechRadioAndRetail=unverified");
            }
        }
        finally
        {
            conversation.Free(); speech.Free(); player.Free(); driver.Free(); File.Delete(Path.Combine(directory.FullName, "Contact.esm")); directory.Delete();
        }
    }

    private static void RejectReputationAdapter(Action action)
    {
        try { action(); } catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
        throw new InvalidDataException("Unqualified native reputation adapter was admitted.");
    }
}
