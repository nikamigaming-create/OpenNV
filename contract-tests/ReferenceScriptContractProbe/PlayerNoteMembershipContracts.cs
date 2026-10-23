using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PlayerNoteMembershipContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-player-notes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Base.esm");
        try
        {
            var header = new byte[12];
            BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            // Deliberately omit NOTE presentation fields: membership must not
            // require decoding text, sound, image or voice to retain identity.
            File.WriteAllBytes(path, Record("TES4", 0, Field("HEDR", header))
                .Concat(Record("NOTE", 1, Field("EDID", Text("FirstNote"))))
                .Concat(Record("NOTE", 2, Field("EDID", Text("SecondNote"))))
                .Concat(Record("GMST", 4, Field("EDID", Text("sComputersAddedNote")), Field("DATA", Text("Collected %s!"))))
                .Concat(Record("MISC", 3, Field("EDID", Text("NotANote")), Field("DATA", new byte[8]))).ToArray());
            using var records = FalloutPluginStack.Load([new("Base.esm", path)]);
            using var world = new OpenNV.Runtime.World.Cells.FalloutReferenceWorld(records);
            var notices = new FalloutHudNotifications();
            world.AcquirePlayerNote(Key(1), notices);
            world.AcquirePlayerNote(Key(2), notices);
            world.AcquirePlayerNote(Key(1), notices);
            Require(world.PlayerNotes.Notes.SequenceEqual(new[] { Key(2), Key(1) }) &&
                notices.Capture().Pending is [{ Event.Text: "Collected !", Event.Kind: FalloutHudEventKind.NoteAdded }],
                "Acquisition invented an EDID title, counted duplicates or lost formatted-text tail deduplication.");
            notices.Advance(0, _ => 2);
            var ordinal = notices.Current!.Ordinal;
            Require(notices.MarkPresented(ordinal) && !notices.MarkPresented(ordinal), "A presented notice replayed its sound handoff.");
            var coldNotices = new FalloutHudNotifications();
            coldNotices.Restore(JsonSerializer.Deserialize<FalloutHudNotificationsSnapshot>(JsonSerializer.Serialize(notices.Capture()))!);
            Require(!coldNotices.MarkPresented(ordinal), "Cold reconstruction replayed an already presented notice.");
            var invalidNotices = new FalloutHudNotifications();
            var invalidBefore = JsonSerializer.Serialize(invalidNotices.Capture());
            var invalidPending = new FalloutHudNotificationsSnapshot(ordinal, null, 0, [notices.Current!]);
            var refusedPending = false;
            try { invalidNotices.Restore(invalidPending); }
            catch (InvalidDataException) { refusedPending = true; }
            Require(refusedPending && JsonSerializer.Serialize(invalidNotices.Capture()) == invalidBefore,
                "A pending notice already marked presented was restored or partially published.");
            invalidNotices.Restore(invalidPending with { Pending = [notices.Current! with { Presented = false }] });
            invalidNotices.Advance(0, _ => 2);
            Require(invalidNotices.MarkPresented(ordinal), "A valid restored pending notice lost its first presentation.");
            coldNotices.Advance(2, _ => 2);
            Require(coldNotices.Current is null, "Source two-second duration did not retire note feedback.");
            world.PlayerNotes.Restore(null);
            Unknown(() => world.AcquirePlayerNote(Key(1), notices));
            var notes = new FalloutPlayerNotes(records);
            Require(notes.Notes.Count == 0 && !notes.Contains(Key(1)), "Fresh player note membership is not empty.");
            Require(notes.AddMembership(Key(1)) && notes.AddMembership(Key(2)), "Distinct notes were not collected.");
            var before = JsonSerializer.Serialize(notes.Capture());
            Require(!notes.AddMembership(Key(1)) && JsonSerializer.Serialize(notes.Capture()) == before,
                "Duplicate note acquisition changed membership order.");
            Require(notes.Notes.SequenceEqual(new[] { Key(2), Key(1) }), "New notes do not prepend in source order.");
            var alias = new FalloutFormKey("base.ESM", 1);
            Require(notes.Contains(alias) && !notes.AddMembership(alias) && JsonSerializer.Serialize(notes.Capture()) == before,
                "Plugin case aliases created distinct note membership.");

            var exposed = (FalloutFormKey[])notes.Notes;
            exposed[0] = Key(3);
            var snapshot = notes.Capture();
            ((FalloutFormKey[])snapshot.Notes)[0] = Key(1);
            Require(JsonSerializer.Serialize(notes.Capture()) == before, "A caller mutated retained note membership through a snapshot.");

            Reject(() => notes.Restore(new([Key(1), Key(3)])));
            Require(JsonSerializer.Serialize(notes.Capture()) == before, "Invalid restored identity partially replaced membership.");
            try
            {
                notes.Restore(new([Key(1), Key(99)]));
                throw new InvalidDataException("A missing NOTE identity was accepted.");
            }
            catch (KeyNotFoundException) { }
            Require(JsonSerializer.Serialize(notes.Capture()) == before, "A missing restored identity partially replaced membership.");
            Reject(() => notes.AddMembership(Key(3)));
            Reject(() => notes.Contains(Key(3)));
            Reject(() => notes.RemoveMembership(Key(3)));
            Require(JsonSerializer.Serialize(notes.Capture()) == before, "Invalid NOTE operations changed retained state.");

            var cold = new FalloutPlayerNotes(records,
                JsonSerializer.Deserialize<FalloutPlayerNotesSnapshot>(before)!);
            Require(JsonSerializer.Serialize(cold.Capture()) == before && cold.Contains(Key(1)) && cold.Contains(Key(2)),
                "Cold restoration changed note membership or order.");
            Require(cold.RemoveMembership(Key(2)) && !cold.RemoveMembership(Key(2)) && !cold.Contains(Key(2)) && cold.Contains(Key(1)),
                "Removing a note changed another identity or retained absent membership.");
            Require(cold.AddMembership(Key(2)) && JsonSerializer.Serialize(cold.Capture()) == before,
                "Reacquisition after cold removal did not restore source order.");
            cold.Restore(new([]));
            Require(cold.Notes.Count == 0, "Restoring an empty collection appended to previous player state.");
            cold.Restore(new([alias, Key(2)]));
            Require(cold.Notes.SequenceEqual(new[] { Key(1), Key(2) }), "Restore did not canonicalize NOTE identity aliases.");

            // Source loading appends saved identities rather than deduplicating
            // them through AddNote. Exact removal consumes the first match only.
            cold.Restore(new([Key(1), Key(2), Key(1)]));
            Require(cold.RemoveMembership(Key(1)) && cold.Notes.SequenceEqual(new[] { Key(2), Key(1) }) && cold.Contains(Key(1)),
                "Restored duplicate membership lost source first-match removal semantics.");
            var inventory = new FalloutPlayerInventory();
            inventory.Add(records, Key(3), 2, 1, silent: true);
            inventory.Remove(Key(3), 2, silent: true);
            Require(cold.Notes.SequenceEqual(new[] { Key(2), Key(1) }), "Ordinary inventory mutation changed collected notes.");

            var knownState = JsonSerializer.Serialize(cold.Capture());
            var knownRevision = cold.Revision;
            Require(!cold.AddMembership(alias) && cold.Revision == knownRevision,
                "Duplicate membership invalidated an unchanged list.");
            foreach (var invalid in new[] { new FalloutFormKey("", 1), Key(0), Key(3) })
            {
                Reject(() => cold.Restore(new([Key(1), invalid])));
                Require(cold.Known && cold.Revision == knownRevision && JsonSerializer.Serialize(cold.Capture()) == knownState,
                    "Invalid replacement changed known membership or its revision.");
            }

            cold.Restore(null);
            var unknownState = JsonSerializer.Serialize(cold.Capture());
            var unknownRevision = cold.Revision;
            Require(!cold.Known && !cold.Capture().Known && cold.Capture().Notes.Count == 0,
                "Missing membership was inferred as known empty.");
            Unknown(() => _ = cold.Notes);
            Unknown(() => cold.Contains(Key(1)));
            Unknown(() => cold.AddMembership(Key(1)));
            Unknown(() => cold.RemoveMembership(Key(1)));
            Reject(() => cold.Restore(new([Key(1)], Known: false)));
            Reject(() => cold.Restore(new([Key(1), Key(3)])));
            Require(!cold.Known && cold.Revision == unknownRevision && JsonSerializer.Serialize(cold.Capture()) == unknownState,
                "Refused operation invented unknown membership or changed its revision.");
            var legacy = new FalloutPlayerNotes(records,
                JsonSerializer.Deserialize<FalloutPlayerNotesSnapshot>(unknownState));
            Require(!legacy.Known && JsonSerializer.Serialize(legacy.Capture()) == unknownState,
                "Cold restoration or resave converted unknown membership to empty.");
            Unknown(() => legacy.Contains(Key(1)));
            legacy.Restore(new([]));
            Require(legacy.Known && legacy.Notes.Count == 0 && !legacy.Contains(Key(1)),
                "Explicit known-empty membership was not distinguished from unknown.");
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_PLAYER_NOTE_MEMBERSHIP_CONTRACT_PASS ordered=true cold=true atomicRestore=true " +
            "typedIdentity=true inventoryIndependent=true unknownRetained=true revisionAtomic=true " +
            "noteNotice=true dedup=true coldPresentationOnce=true nativeAudio=unverified ordinaryGameplay=unverified");
    }

    private static FalloutFormKey Key(uint id) => new("Base.esm", id);
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("A non-NOTE identity was accepted by player note state.");
    }
    private static void Unknown(Action action)
    {
        try { action(); }
        catch (NotSupportedException error) when (error.Message.Contains("no authoritative collected-note membership", StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("Unknown membership supplied a fabricated answer or mutation.");
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length));
        data.CopyTo(bytes, 6);
        return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = fields.SelectMany(bytes => bytes).ToArray();
        var bytes = new byte[24 + data.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24);
        return bytes;
    }
}
