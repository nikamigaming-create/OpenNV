using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal float? EvaluatePlayerNoteCondition(FalloutCondition condition)
    {
        if (condition.Function != 382) return null;
        if (condition.RunOn > 2)
            throw new NotSupportedException("GetHasNote condition run-on context is unbound.");
        if (condition.RunOn == 2 && condition.Reference != 0)
        {
            var reference = condition.Owner.Plugin.AdjustFormId(condition.Reference);
            if (records.RuntimeFormId(reference) != 0x14 &&
                (!records.TryGetEffective(reference, out var placed) || placed.Signature is not ("REFR" or "ACHR" or "ACRE")))
                throw new InvalidDataException("GetHasNote explicit context is not an actual placed reference.");
        }
        return PlayerNotes.Contains(condition.FormArgument1) ? 1 : 0;
    }

    internal void AcquirePlayerNote(FalloutFormKey note, FalloutHudNotifications notifications)
    {
        if (PlayerNotes.Contains(note)) return;
        var record = records.GetEffective(note);
        var fullName = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
        var name = fullName.IsEmpty ? string.Empty : FalloutDialogueTopic.Text(fullName.Span);
        var format = FalloutGameSettingStrings.Read(records, "sComputersAddedNote");
        var text = new System.Text.StringBuilder(); var consumed = false;
        for (var index = 0; index < format.Length; ++index)
        {
            if (format[index] != '%') { text.Append(format[index]); continue; }
            if (++index == format.Length) throw new InvalidDataException("Note feedback format is incomplete.");
            if (format[index] == '%') { text.Append('%'); continue; }
            if (format[index] != 's' || consumed) throw new NotSupportedException("Note feedback format is unsupported.");
            text.Append(name); consumed = true;
        }
        if (!consumed) throw new InvalidDataException("Note feedback format omitted its NOTE name.");
        var notice = new FalloutHudEvent(FalloutHudEventKind.NoteAdded, record.FormKey, 0, Text: text.ToString());
        notifications.RequirePublish([notice]);
        PlayerNotes.AddMembership(record.FormKey);
        notifications.PublishNote(notice);
    }

    internal static void RequirePlayerItemAcquisition(FalloutPluginRecord item)
    {
        if (item.Signature == "NOTE")
            throw new NotSupportedException("Collected-note world/container handoff requires its ownership and retirement owners.");
    }
}
