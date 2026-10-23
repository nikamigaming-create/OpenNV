using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativePipBoyAudit
{
    private async Task ExerciseNoteRemoval(NativeOwnedPipBoyMenu menu, FalloutPluginStack records,
        FalloutReferenceWorld references, FalloutPipBoyState state, FalloutPlayerInventory inventory)
    {
        // Controlled membership uses actual owned text NOTE records and the
        // actual menu. This does not assert ordinary acquisition or pickup.
        var notes = records.EffectiveRecords("NOTE").Where(record =>
            record.ReadSubrecords().Any(field => field.Signature == "DATA" && field.Data.Span.SequenceEqual(new byte[] { 1 })) &&
            record.ReadSubrecords().Any(field => field.Signature == "TNAM"))
            .Take(2).Select(record => FalloutNote.Read(records, record.FormKey)).ToArray();
        if (notes.Length != 2 || notes[0].Name == notes[1].Name) throw new InvalidDataException("Owned note UI fixture needs two distinct text labels.");
        var hashes = notes.Select(note => SHA256.HashData(note.Record.ReadData())).ToArray();
        references.PlayerNotes.Restore(new(notes.Select(note => note.Record.FormKey).ToArray()));
        menu.Select(FalloutPipBoyPage.Data, 3);
        if (menu.Error is not null) throw new InvalidDataException(menu.Error);
        if (state.SelectedNote is null) throw new InvalidDataException("Visiting an empty notes list suppressed later first-note selection.");
        menu.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == notes[0].Name)
            .EmitSignal(BaseButton.SignalName.Pressed);
        if (state.SelectedNote != notes[0].Record.FormKey) throw new InvalidDataException("Actual note click did not select its identity.");
        var hud = JsonSerializer.Serialize(inventory.Notifications.Capture());
        references.RemovePlayerNote(notes[0].Record.FormKey, state.InvalidateNote);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        menu.Select(FalloutPipBoyPage.Data, 3);
        if (menu.Error is not null || state.SelectedNote is not null ||
            references.PlayerNotes.Contains(notes[0].Record.FormKey) || !references.PlayerNotes.Contains(notes[1].Record.FormKey))
            throw new InvalidDataException("Live note removal retained selection or forced another note selection.");
        var cold = JsonSerializer.Deserialize<FalloutPlayerNotesSnapshot>(JsonSerializer.Serialize(references.PlayerNotes.Capture()));
        references.PlayerNotes.Restore(cold);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (state.SelectedNote is not null || menu.Error is not null) throw new InvalidDataException("Membership cold refresh reselected a removed note.");
        menu.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == notes[1].Name)
            .EmitSignal(BaseButton.SignalName.Pressed);
        var revision = state.Revision;
        references.RemovePlayerNote(notes[0].Record.FormKey, state.InvalidateNote);
        if (state.SelectedNote != notes[1].Record.FormKey || state.Revision != revision ||
            JsonSerializer.Serialize(inventory.Notifications.Capture()) != hud)
            throw new InvalidDataException("Unrelated removal changed selected note or HUD feedback.");
        for (var index = 0; index < notes.Length; ++index)
            if (!SHA256.HashData(notes[index].Record.ReadData()).SequenceEqual(hashes[index])) throw new InvalidDataException("Owned NOTE source changed.");
        GD.Print("OPENNV_OWNED_REMOVE_NOTE_UI_PASS actualOwnedTextNotes=true actualClick=true exactInvalidation=true " +
            "noForcedReselection=true coldMembership=true hudUnchanged=true controlledMembership=true ordinaryPickup=false retailParity=unverified");
    }
}
