using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutFinishedSpeechSourceBinding
{
    internal static FalloutSpeechSpeakerDomain Speakers(FalloutPluginStack records)
    {
        bool Candidate(FalloutFormKey form)
        {
            if (!records.TryGetEffective(form, out var record) || record.IsDeleted || record.Signature is not ("ACHR" or "ACRE" or "REFR")) return false;
            var names = record.ReadSubrecords().Where(field => field.Signature == "NAME").ToArray();
            if (names.Length != 1 || names[0].Data.Length != 4) return false;
            var key = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(names[0].Data.Span));
            return records.TryGetEffective(key, out var basis) && !basis.IsDeleted &&
                (record.Signature == "ACHR" && basis.Signature == "NPC_" || record.Signature == "ACRE" && basis.Signature == "CREA" ||
                 record.Signature == "REFR" && basis.Signature == "TACT");
        }
        // The complete placed-reference count bounds this finite saved set.
        // Validate selected speakers lazily; reading every irrelevant REFR
        // payload on a cold save is neither necessary nor a source index.
        var bound = checked(new[] { "ACHR", "ACRE", "REFR" }.Sum(signature => records.EffectiveRecords(signature).Count()));
        return new(bound, Candidate);
    }

    internal static FalloutFinishedSpeechReceipt Capture(FalloutPluginStack records, FalloutReferenceInstance instance,
        FalloutSpeechCompletionReceipt receipt)
    {
        if (receipt.Info is not { } info) throw new InvalidDataException("Finished speech has no source INFO.");
        var result = new FalloutFinishedSpeechReceipt(receipt.Speaker, receipt.Topics.ToArray(), info, receipt.Generation,
            new(InfoHash(records.GetEffective(info)), instance.Script?.Record.FormKey, instance.Script?.Sha256));
        Require(records, instance, result);
        return result;
    }

    internal static void Require(FalloutPluginStack records, FalloutReferenceInstance instance, FalloutFinishedSpeechReceipt receipt)
    {
        receipt.Source.Validate();
        _ = receipt.Completion();
        var speaker = records.GetEffective(receipt.Speaker);
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(speaker, "NAME"));
        if (instance.Reference != receipt.Speaker || instance.DeletePending || instance.Deleted || instance.Templates?.Absent == true ||
            speaker.IsDeleted || basis.IsDeleted || !(speaker.Signature == "ACHR" && basis.Signature == "NPC_" ||
                speaker.Signature == "ACRE" && basis.Signature == "CREA" || speaker.Signature == "REFR" && basis.Signature == "TACT"))
            throw new InvalidDataException("Saved finished speech has no retained winning speaker.");
        var info = records.GetEffective(receipt.Info);
        var groups = info.Groups.Where(group => group.Type == 7).ToArray();
        if (info.Signature != "INFO" || info.IsDeleted || groups.Length != 1 || receipt.Topics.Count != 1 ||
            !receipt.Topics.Contains(info.Plugin.AdjustFormId(groups[0].LabelAsUInt32)) ||
            receipt.Topics.Any(topic => records.GetEffective(topic).Signature != "DIAL") ||
            receipt.Source.InfoSha256 != InfoHash(info) || receipt.Source.SpeakerScript != instance.Script?.Record.FormKey ||
            receipt.Source.SpeakerScriptSha256 != instance.Script?.Sha256)
            throw new InvalidDataException("Saved finished speech differs from its winning INFO/DIAL/script binding.");
    }

    private static string InfoHash(FalloutPluginRecord info)
    {
        if (info.Signature != "INFO") throw new InvalidDataException("Finished speech source is not INFO.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(info.ReadData()); hash.AppendData(Encoding.UTF8.GetBytes(info.FormKey + "\0"));
        foreach (var group in info.Groups.Where(group => group.Type == 7))
            hash.AppendData(Encoding.UTF8.GetBytes(info.Plugin.AdjustFormId(group.LabelAsUInt32) + "\0"));
        foreach (var field in info.ReadSubrecords().Where(field => field.Signature == "SCRO"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Finished INFO has malformed script references.");
            hash.AppendData(Encoding.UTF8.GetBytes(info.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) + "\0"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
