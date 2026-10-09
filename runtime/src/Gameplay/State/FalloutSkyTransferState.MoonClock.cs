using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutMoonHourStore(Guid Process, FalloutMoonCalendarSource Calendar,
    FalloutFormKey Cell, uint HourBits, long Changed);

internal sealed partial class FalloutSkyTransferState
{
    private FalloutMoonHourStore? _moonHourStore;
    internal FalloutMoonHourStore? SourceMoonHourStore => _moonHourStore;
    internal void StoreSourceMoonCalendarHour(FalloutMoonPlayerFrame player, FalloutMoonCalendarSample calendar)
    {
        BindPresentationThread(); RequireWriter();
        if (player.Process != _process || !float.IsFinite(BitConverter.UInt32BitsToSingle(calendar.HourBits)) ||
            calendar.Source.Hour != _records.RuntimeFormKey(0x38) || calendar.Source.Days != _records.RuntimeFormKey(0x39))
            throw new InvalidDataException("Sky hour store changed the actual Player/process/calendar declaration.");
        // The selected standalone frame checks actual Player.currentCELL, then
        // calls the shared GameHour getter and stores its Float32 cell.
        if (!Source.IsStandalone)
            throw new NotSupportedException("source-FNV-Sky-frame-GameHour-store-caller-contract-unowned");
        if (_moonHourStore is null) calendar.Source.Require(_records, Source);
        else if (_moonHourStore.Calendar != calendar.Source)
            throw new InvalidDataException("Sky frame replaced the actual immutable calendar/global source owner.");
        HourBits = calendar.HourBits;
        _moonHourStore = new(_process, calendar.Source, player.Cell, HourBits, Next());
        _clockFailure = null;
    }
    private static void ValidateMoonHourStore(FalloutSkyTransferSnapshot saved, FalloutSkyTransferDeclaration source,
        FalloutPluginStack records)
    {
        if (saved.MoonHourStore is not { } store)
        {
            if (saved.HourBits != source.ConstructorClockBits)
                throw new InvalidDataException("Saved Sky hour changed without its actual Player/calendar-store receipt.");
            return;
        }
        if (!source.IsStandalone || store.Process != saved.CapturedProcess || store.Changed < 1 || store.Changed > saved.Changed ||
            store.HourBits != saved.HourBits || store.Calendar.Hour != records.RuntimeFormKey(0x38) ||
            store.Calendar.Days != records.RuntimeFormKey(0x39) ||
            records.GetEffective(store.Cell).Signature != "CELL" ||
            store.Calendar.HourSha256.Length != 64 || store.Calendar.DaysSha256.Length != 64 || store.Calendar.CalendarSha256.Length != 64)
            throw new InvalidDataException("Saved Sky hour is not a current source Player/CELL/calendar store.");
        store.Calendar.Require(records, source);
    }
    private void RestoreMoonHourStore(FalloutSkyTransferSnapshot saved, Guid process)
    {
        _moonHourStore = saved.MoonHourStore is { } old ? old with { Process = process, Changed = Next() } : null;
    }
}
