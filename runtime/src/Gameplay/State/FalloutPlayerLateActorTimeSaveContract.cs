using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutPlayerLateActorTimeSaveContract
{
    // Both capture and cold admission call this with the real parent snapshots.
    // Latest reads may belong to retained older publications and older values.
    internal static void Validate(FalloutPlayerLateActorSnapshot actor, FalloutPackageCommandFieldSnapshot fields,
        FalloutGlobalStateSnapshot globals, FalloutActorUpdateSnapshot updates, FalloutFormKey actualDay)
    {
        FalloutPlayerLateActorTimeState.Validate(actor);
        var fieldSource = FalloutPackageCommandFieldsSource.Reviewed(actor.Source.Following.Controls);
        FalloutActorPackageCommandFields.Validate(fields, fieldSource, actor.Stack, actor.Actor);
        var initializer = fields.ScriptInitialization ?? throw new InvalidDataException("Actor-time lost its real reference flag initializer parent.");
        var flagHistory = new FalloutReferenceFlagHistory(initializer,
            FalloutReferenceScriptExtraInitializerSource.Reviewed(fieldSource), actor.Stack, actor.Actor);
        var referencePublications = initializer.Publications.ToDictionary(row => row.Process);
        foreach (var publication in actor.Publications)
            if (!referencePublications.TryGetValue(publication.CommonProcess, out var reference) ||
                reference.ThreadProcess != publication.PlayerProcess || reference.Constructor != publication.PlayerConstructor || reference.Epoch != publication.Epoch)
                throw new InvalidDataException("Actor-time history has no actual corresponding reference/Common/Scene publication.");
        var calendar = globals.PlayerFrameCalendar ?? throw new InvalidDataException("Actor-time lost its actual shared GLOBAL frame-calendar owner.");
        FalloutGlobalState.ValidatePlayerFrameCalendar(calendar, globals.Values);
        if (calendar.Source != actor.Source.Identity || calendar.Stack != actor.Stack || calendar.Form != actualDay ||
            calendar.Process != actor.PlayerProcess || !calendar.Publications.Select(row => row.Process).SequenceEqual(actor.Publications.Select(row => row.PlayerProcess)))
            throw new InvalidDataException("Actor-time calendar changed the actual reserved day GLOBAL or current/cold Scene publication chain.");
        if (actor.LastCall is { } call)
        {
            if (call.ReferenceFlags is { } first) flagHistory.RequirePlayerFrame(first, call.Invocation);
            if (call.ActorValueFlags is { } values) flagHistory.RequirePlayerFrame(values, call.Invocation);
            if (call.CalendarDay is { } day && day != calendar.LastRead)
                throw new InvalidDataException("Actor-time calendar read has no identical retained actual GLOBAL getter publication.");
        }
        FalloutActorUpdateState.ValidateFrameGlobalAiParent(updates, actor.Source.Identity, actor.Stack);
        FalloutPlayerLateActorTimeState.ValidateSharedAiRead(actor, updates);
    }

    internal static void ValidateOwned(FalloutPluginStack records, FalloutPlayerLateActorSnapshot actor,
        FalloutPackageCommandFieldSnapshot fields, FalloutGlobalStateSnapshot globals, FalloutActorUpdateSnapshot updates)
    {
        var day = FalloutGameTimeBindings.Read(records).Day;
        Validate(actor, fields, globals, updates, day);
        var winning = FalloutGlobal.Read(records.GetEffective(day));
        if (globals.PlayerFrameCalendar!.WinningSha256 != winning.SourceSha256)
            throw new InvalidDataException("Frame calendar differs from the player's complete winning reserved-day GLOBAL record.");
    }
}
