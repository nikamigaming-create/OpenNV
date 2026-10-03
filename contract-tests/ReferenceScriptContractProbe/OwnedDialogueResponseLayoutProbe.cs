using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static class OwnedDialogueResponseLayoutProbe
{
    internal static void Run(string mod, string root, string baseRoot, string topicId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var topic = FalloutDialogueTopic.Read(records, topicId);
            var empty = records.EffectiveRecords("INFO")
                .Where(record => !record.ReadSubrecords().Any(field => field.Signature == "TRDT")).ToArray();
            if (empty.Length == 0 || !topic.Infos.Any(info => info.Responses.Count == 0) ||
                !topic.Infos.Any(info => info.Responses.Count != 0))
                throw new InvalidDataException("Selected source topic does not cover both response layouts.");
            var scripts = 0;
            foreach (var record in empty)
            {
                var before = SHA256.HashData(record.ReadData());
                var info = FalloutDialogueTopic.Decode(record);
                if (info.Responses.Count != 0 || info.Record.FormKey != record.FormKey ||
                    !before.SequenceEqual(SHA256.HashData(record.ReadData())))
                    throw new InvalidDataException("Empty INFO acquired a response, lost identity or changed source bytes.");
                if (info.BeginScript.Length != 0 || info.EndScript.Length != 0) ++scripts;
            }
            Console.WriteLine($"OPENNV_OWNED_DIALOGUE_RESPONSE_LAYOUT_PASS emptyInfos={empty.Length} resultScripts={scripts} " +
                $"topicInfos={topic.Infos.Count} sourceReadonly=true fabricatedResponses=false selectedEmptyBehavior=unbound ordinaryInput=separate");
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
