using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    // This is a completion of speech already admitted by its engine owner,
    // not an ordinary off-cell script frame or a new reference incarnation.
    internal FalloutReferenceScriptEventResult DispatchSpeechCompletion(FalloutSpeechCompletionReceipt receipt)
    {
        var speaker = records.GetEffective(receipt.Speaker);
        if (speaker.Signature is not ("ACHR" or "ACRE" or "REFR") || speaker.IsDeleted)
            throw new InvalidDataException("Speech completion speaker is not a winning dialogue reference.");
        var actor = records.GetEffective(FalloutDialogueTopic.RequiredForm(speaker, "NAME"));
        if (!(speaker.Signature == "ACHR" && actor.Signature == "NPC_" ||
            speaker.Signature == "ACRE" && actor.Signature == "CREA" ||
            speaker.Signature == "REFR" && actor.Signature == "TACT") || actor.IsDeleted)
            throw new InvalidDataException("Speech completion speaker has no source dialogue owner.");
        foreach (var topic in receipt.Topics)
            if (records.GetEffective(topic).Signature != "DIAL")
                throw new InvalidDataException("Speech completion topic is not DIAL.");
        if (receipt.Info is { } infoKey)
        {
            var info = records.GetEffective(infoKey);
            var topics = info.Groups.Where(group => group.Type == 7).ToArray();
            if (info.Signature != "INFO" || info.IsDeleted || topics.Length != 1 ||
                !receipt.Topics.Contains(info.Plugin.AdjustFormId(topics[0].LabelAsUInt32)))
                throw new InvalidDataException("Speech completion INFO does not belong to its winning source topic.");
        }
        var instance = world.Retained(receipt.Speaker);
        if (instance.DeletePending || instance.Deleted || instance.Templates?.Absent == true)
            throw new InvalidOperationException("Speech completion speaker no longer owns a live retained instance.");
        // A source fault may have consumed writes and effects. An asynchronous
        // completion never recovers it or re-enters its historical prefix.
        if (instance.ScriptError is { } error) return new(receipt.Speaker, "SayToDone", 0, error);
        var previous = _speechInvocation;
        _speechInvocation = receipt.Info is null ? null : FalloutFinishedSpeechSourceBinding.Capture(records, instance, receipt);
        try { return DispatchFrameCore(instance, [new("SayToDone", Topics: receipt.Topics)], 0).Single(); }
        finally { _speechInvocation = previous; }
    }
}
