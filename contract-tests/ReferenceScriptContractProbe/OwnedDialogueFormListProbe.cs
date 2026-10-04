using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedDialogueFormListProbe
{
    internal static void Run(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "DialogueRepublicDave");
        var caller = FalloutDialogueTopic.Find(records, "ACHR", "CG03DadREF");
        var conditions = FalloutCondition.Read(quest).Where(value => value.Function == 372).ToArray();
        if (conditions.Length != 1) throw new InvalidDataException("Source dialogue has no unique IsInList filter.");
        var condition = conditions[0];
        var list = records.GetEffective(condition.FormArgument1);
        if (list.Signature != "FLST" || condition.RunOn != 0 || condition.Comparison != 1)
            throw new InvalidDataException("Source dialogue list scope or comparison changed.");
        var members = list.ReadSubrecords().Where(field => field.Signature == "LNAM").Select(field =>
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Source list member extent is invalid.");
            return list.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
        }).ToHashSet();
        var member = records.EffectiveRecords("ACHR").First(reference =>
            members.Contains(FalloutDialogueTopic.RequiredForm(reference, "NAME")));
        var sources = new[] { quest, list, caller, member };
        var hashes = sources.Select(source => SHA256.HashData(source.ReadData())).ToArray();
        var quests = new FalloutQuestState(records);
        var before = JsonSerializer.Serialize(quests.Capture());
        FalloutDialogueConditions Context(FalloutPluginRecord speaker, FalloutFormKey? listener = null) =>
            new(records, quests, speaker.FormKey,
                FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(speaker, "NAME")),
                runtime: _ => throw new InvalidOperationException("Source membership escaped its identity owner."), listener: listener);
        if (Context(caller).Evaluate(condition) != 0 || Context(member).Evaluate(condition) != 1 ||
            Context(caller, member.FormKey).Evaluate(condition with { RunOn = 1 }) != 1 ||
            before != JsonSerializer.Serialize(quests.Capture()) ||
            sources.Where((source, index) => !hashes[index].SequenceEqual(SHA256.HashData(source.ReadData()))).Any())
            throw new InvalidDataException("Source list membership confused bases/references or changed quest/source state.");
        var perkQuest = FalloutDialogueTopic.Find(records, "QUST", "FreeformPowerArmor");
        var perkCondition = FalloutCondition.Read(perkQuest).Single(value => value.Function == 449);
        if (perkCondition.RunOn != 2 || perkCondition.Argument2 != 0 || perkCondition.Comparison != 0 ||
            perkQuest.Plugin.AdjustFormId(perkCondition.Reference) != records.RuntimeFormKey(0x14))
            throw new InvalidDataException("Source player perk filter changed its subject/rank scope.");
        var acquired = new List<FalloutFormKey>();
        var inventory = new FalloutPlayerInventory();
        var skills = new FalloutPlayerSkills(records, () => throw new InvalidOperationException("Perk query requested SPECIAL."),
            _ => false, () => [], null, inventory, records.RuntimeFormKey(7),
            () => throw new InvalidOperationException("Perk query requested race."), () => false, () => acquired);
        float QueryPerk(FalloutCondition value) => FalloutInventoryConditions.EvaluateDialoguePlayer(records, inventory, skills.HasPerk, value)
            ?? throw new NotSupportedException("Source player perk subject escaped its authoritative owner.");
        var perkDialogue = new FalloutDialogueConditions(records, quests, caller.FormKey,
            FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(caller, "NAME")), runtime: QueryPerk);
        if (perkDialogue.Evaluate(perkCondition) != 0) throw new InvalidDataException("Source perk filter invented a grant.");
        // This is an isolated query fixture, never a campaign-stage effect.
        acquired.Add(perkCondition.FormArgument1);
        if (perkDialogue.Evaluate(perkCondition) != 1 || acquired.Count != 1 ||
            before != JsonSerializer.Serialize(quests.Capture()))
            throw new InvalidDataException("Source perk query lost current player ownership or mutated quest state.");
        using var world = new FalloutReferenceWorld(records);
        var zoneCondition = FalloutCondition.Read(perkQuest).Single(value => value.Function == 446);
        var zoneDialogue = new FalloutDialogueConditions(records, quests, caller.FormKey,
            FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(caller, "NAME")),
            referenceInZone: (reference, zone) => world.IsInZone(reference, zone, null, 1));
        var zoneBefore = JsonSerializer.Serialize(world.CaptureEncounterZones());
        if (zoneDialogue.Evaluate(zoneCondition) != 0 || zoneBefore != JsonSerializer.Serialize(world.CaptureEncounterZones()))
            throw new InvalidDataException("Original unrelated encounter-zone filter invented residency or initialized encounter state.");
        Console.WriteLine($"OPENNV_OWNED_DIALOGUE_FORM_LIST_PASS quest={quest.FormKey} list={list.FormKey} " +
            $"outside={caller.FormKey} member={member.FormKey} sourceBase=true sourceMasters=true outsideFalse=true memberTrue=true " +
            "listener=true readonly=true sourceUnchanged=true playerPerkSourceScope=true currentPlayerPerks=true sourceZoneFilter=false queryOnlyZone=true campaignInput=separate parity=unverified");
    }
}
