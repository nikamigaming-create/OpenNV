using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerFrameCalendarReadOwner(string Source, string Stack,
    Guid Process, Guid Factory, long Sequence, long ValueRevision, FalloutPlayerSourceCaller Caller,
    Guid Constructor, Guid CommonProcess, long Epoch, int ThreadId);
internal sealed record FalloutPlayerFrameCalendarPublication(Guid Process, Guid Factory, long Changed,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutPlayerFrameCalendarSnapshot(string Source, string Stack,
    Guid Process, Guid Factory, FalloutFormKey Form, string WinningSha256, long Sequence,
    long ValueRevision, FalloutPlayerLateCalendarDayRead? LastRead,
    IReadOnlyList<FalloutPlayerFrameCalendarPublication> Publications);
