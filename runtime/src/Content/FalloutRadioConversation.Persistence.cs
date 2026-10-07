using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutFinishedRadioConversationSnapshot(FalloutRadioStation Station,
    string ReferenceSha256, string BaseSha256, FalloutFormKey Topic, string TopicSha256, long CompletedLines);

internal sealed partial class FalloutRadioConversation
{
    internal FalloutFinishedRadioConversationSnapshot CaptureFinishedState()
    {
        if (Active) throw new NotSupportedException("Active radio requires its audio/result continuation.");
        var station = Station ?? throw new InvalidOperationException("Ended radio has no original station.");
        var topic = Topic ?? throw new InvalidOperationException("Ended radio has no original topic.");
        var snapshot = new FalloutFinishedRadioConversationSnapshot(station,
            FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(station.Reference)),
            FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(station.Base)), topic,
            FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(topic)), CompletedLines);
        ValidateFinishedState(records, snapshot);
        return snapshot;
    }

    internal static void ValidateFinishedState(FalloutPluginStack records, FalloutFinishedRadioConversationSnapshot snapshot)
    {
        if (snapshot.Station is null || !FalloutActorFurnitureContinuation.ValidKey(snapshot.Station.Reference) ||
            !FalloutActorFurnitureContinuation.ValidKey(snapshot.Station.Base) ||
            !FalloutActorFurnitureContinuation.ValidKey(snapshot.Topic) || snapshot.CompletedLines < 0 || snapshot.CompletedLines == long.MaxValue)
            throw new InvalidDataException("Ended radio has invalid station, topic or completion history.");
        var reference = records.GetEffective(snapshot.Station.Reference);
        var station = FalloutRadioStation.Read(records, reference);
        var topic = records.GetEffective(snapshot.Topic);
        if (station != snapshot.Station || topic.Signature != "DIAL" || FalloutDialogueTopic.Type(records, snapshot.Topic) != 7 ||
            !HashMatches(reference, snapshot.ReferenceSha256) || !HashMatches(records.GetEffective(station.Base), snapshot.BaseSha256) ||
            !HashMatches(topic, snapshot.TopicSha256))
            throw new InvalidDataException("Ended radio differs from its winning station or radio topic.");
    }

    internal void RestoreFinishedState(FalloutFinishedRadioConversationSnapshot snapshot)
    {
        if (Station is not null || Topic is not null || Info is not null || CompletedLines != 0)
            throw new InvalidOperationException("Ended radio restoration needs a fresh inactive owner.");
        ValidateFinishedState(records, snapshot);
        // Selection, source results, audio and RNG have already finished. Restore
        // their actual ended history without evaluating another line.
        Station = snapshot.Station; Topic = snapshot.Topic; CompletedLines = snapshot.CompletedLines;
    }

    private static bool HashMatches(FalloutPluginRecord record, string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit) &&
        FalloutActorFurnitureContinuation.RecordHash(record).Equals(hash, StringComparison.OrdinalIgnoreCase);
}
