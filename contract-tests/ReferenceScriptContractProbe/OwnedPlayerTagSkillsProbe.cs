using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedPlayerTagSkillsProbe
{
    internal static void Ttw(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
        var contract = FalloutNativeTagSkillResolver.Resolve(records, controls);
        var test = FalloutDialogueTopic.Find(records, "QUST", "CG03Test");
        var sourceHash = SHA256.HashData(test.ReadData());
        var reached = new HashSet<uint>();
        for (short stage = 1; stage <= 13; ++stage)
        {
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var tags = new FalloutPlayerTagSkills(records, contract);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Original scoring stage escaped its C# owner."), TagSkills: tags));
            var results = new FalloutQuestStages(records, quests, scripts.StageSteps, quests.Evaluate);
            results.Enter(test.FormKey, stage);
            if (results.HasUnfinishedResults || tags.Selection.Count != 1 || tags.Capture().Slots[0] is null || world.InstanceCount != 0)
                throw new InvalidDataException("Original scoring result did not complete its indexed source assignment.");
            reached.Add(tags.Selection[0].RuntimeFormId);
            var snapshot = JsonSerializer.Deserialize<FalloutPlayerTagSkillsSnapshot>(JsonSerializer.Serialize(tags.Capture()))!;
            var cold = new FalloutPlayerTagSkills(records, contract, snapshot);
            if (JsonSerializer.Serialize(cold.Capture()) != JsonSerializer.Serialize(snapshot))
                throw new InvalidDataException("Original scoring tag assignment changed during cold restoration.");
        }
        if (reached.Count != contract.Skills.Count || !sourceHash.AsSpan().SequenceEqual(SHA256.HashData(test.ReadData())))
            throw new InvalidDataException("Original scoring stages missed a skill or changed owned source bytes.");
        var handIn = records.GetEffective(new("Fallout3.esm", 0x035efb));
        var handInHash = SHA256.HashData(handIn.ReadData());
        var command = handIn.ReadSubrecords().Where(field => field.Signature == "SCTX")
            .SelectMany(field => FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)))
            .Single(line => line.StartsWith("SetTagSkills ", StringComparison.OrdinalIgnoreCase));
        var request = FalloutTagSkillMenuRequest.Read(command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1..]);
        if (request != new FalloutTagSkillMenuRequest(3, true) || !handInHash.AsSpan().SequenceEqual(SHA256.HashData(handIn.ReadData())))
            throw new InvalidDataException("Original hand-in menu lost its optional default or changed owned source bytes.");
        Console.WriteLine("OPENNV_OWNED_TTW_TAG_SLOTS_PASS originalScoringStages=13 skills=13 sourceAssignments=true " +
            "cold=true sourceHandInOptionalMenu=true sourceReadonly=true enginePlayerRecordAbsent=true boundary=isolated-scoring-not-campaign-progress");
    }
}
