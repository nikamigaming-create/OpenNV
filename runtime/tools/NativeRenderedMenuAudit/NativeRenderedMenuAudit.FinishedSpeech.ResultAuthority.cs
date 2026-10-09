using System.Security.Cryptography;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task FinishedSpeechResultAuthorityNegative(string game, string mod, string modRoot,
        string actorId, string topicId, string questId, short stage, string[] dependencies)
    {
        RuntimeNativeNpc? body = null;
        FalloutReferenceWorld? world = null;
        FalloutPluginStack? records = null;
        try
        {
            var setup = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            records = FalloutPluginStack.Load(content.PluginSources);
            world = new FalloutReferenceWorld(records);
            var actor = FalloutDialogueTopic.Find(records, "ACHR", actorId);
            var topic = FalloutDialogueTopic.Read(records, topicId);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records);
            quests.SetRunning(quest, true); quests.EnterStage(quest, stage);
            var cell = FalloutCellSceneReader.Read(records, world.Get(actor.FormKey).Cell);
            world.LoadCell(cell);
            // Declared fixture enable/stage setup admits the real source body.
            // No object script, stopped source cursor or campaign save executes.
            FinishedSpeechEnableActor(records, world, actor.FormKey);
            var configuration = RuntimeConfiguration.Load();
            body = FinishedSpeechBody(records, content, world, cell, actor.FormKey, configuration.World.GameUnitsToMeters);
            AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
            ConfigureFinishedSpeechLook(body, records, content, world);
            var inputs = new[] { actor, records.GetEffective(world.Get(actor.FormKey).Base), topic.Topic,
                records.GetEffective(quest), records.GetEffective(cell.Cell.FormKey) };
            string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
            var hashes = inputs.Select(Hash).ToArray();
            await FinishedSpeechResultNegative(records, configuration, world, quests, body, actor.FormKey, topic.Topic.FormKey);
            RequireFinished(!inputs.Where((record, index) => Hash(record) != hashes[index]).Any(),
                "Native result authority fixture changed owned source records.");
        }
        finally
        {
            try { body?.Free(); }
            finally
            {
                try { world?.Dispose(); }
                finally { records?.Dispose(); RuntimeLiveContentSource.Clear(); }
            }
        }
    }
}
