using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerLateActorTimeState
{
    internal static void Validate(FalloutPlayerLateActorSnapshot saved)
    {
        saved.Source.Validate(); saved.Actor.Validate();
        if (string.IsNullOrWhiteSpace(saved.Stack) || !saved.Actor.EnginePlayer || saved.Epoch < 1 ||
            saved.PlayerProcess == Guid.Empty || saved.Factory == Guid.Empty || saved.PlayerConstructor == Guid.Empty ||
            saved.Sequence < 0 || saved.Calls < 0 || saved.Publications is not { Count: > 0 } ||
            saved.Fields is null || saved.Fields.MobileState != byte.MaxValue || saved.Fields.SelfPredicate > 1 ||
            saved.Fields.DateMismatch > 1 || saved.Fields.AiBlocked != 0 ||
            saved.Failure is { Length: 0 } || saved.WriterBoundary is { Length: 0 } ||
            (saved.Calls == 0) != (saved.LastCall is null) ||
            saved.Calls == 0 && (saved.Fields != FalloutPlayerLateActorFields.Constructed || saved.Failure is not null))
            throw new InvalidDataException("Actor-time state lost its exact source constructor or current field lineage.");
        var publications = new Dictionary<Guid, FalloutPlayerLateActorPublication>();
        var processes = new HashSet<Guid>(); var constructors = new HashSet<Guid>(); long prior = -1;
        for (var index = 0; index < saved.Publications.Count; index++)
        {
            var row = saved.Publications[index];
            if (row.PlayerProcess == Guid.Empty || row.Factory == Guid.Empty || row.PlayerConstructor == Guid.Empty ||
                row.CommonProcess == Guid.Empty || row.Actor != saved.Actor || row.Epoch != saved.Epoch ||
                row.Published <= prior || row.Published > saved.Sequence ||
                !publications.TryAdd(row.Factory, row) || !processes.Add(row.PlayerProcess) || !constructors.Add(row.PlayerConstructor) ||
                (index == 0 ? row.Published != 0 || row.PreviousProcess is not null || row.PreviousFactory is not null :
                    row.PreviousProcess != saved.Publications[index - 1].PlayerProcess || row.PreviousFactory != saved.Publications[index - 1].Factory))
                throw new InvalidDataException("Actor-time lost its ordered fresh process/constructor publications.");
            prior = row.Published;
        }
        var current = saved.Publications[^1];
        if (current.PlayerProcess != saved.PlayerProcess || current.Factory != saved.Factory || current.PlayerConstructor != saved.PlayerConstructor)
            throw new InvalidDataException("Actor-time current fields promoted a historical source factory.");
        if (saved.LastCall is not { } call) return;
        if (call.Source != saved.Source.Identity || call.Stack != saved.Stack || call.Ordinal != saved.Calls ||
            call.Entered < 1 || call.Changed <= call.Entered || call.Changed > saved.Sequence ||
            !Enum.IsDefined(call.Step) || call.Stores is null || call.Stores.Count > 4 ||
            call.Before is null || call.After != saved.Fields || call.BeforeScalars is null || call.AfterScalars is null ||
            call.Failure != saved.Failure || call.Returned != (call.Failure is null) ||
            call.Returned != (call.Step == FalloutPlayerLateActorTimeStep.Returned) ||
            (call.FailedAt is not null) == call.Returned || call.FailedAt is { } failed && (!Enum.IsDefined(failed) ||
                failed is FalloutPlayerLateActorTimeStep.Returned or FalloutPlayerLateActorTimeStep.Failed) ||
            !publications.TryGetValue(call.Factory, out var owner) || call.Invocation.PlayerProcess != owner.PlayerProcess ||
            call.Invocation.PlayerConstructor != owner.PlayerConstructor || call.Invocation.Common.Process != owner.CommonProcess ||
            call.Invocation.Common.Actor != saved.Actor || call.Invocation.Common.Epoch != saved.Epoch ||
            call.Invocation.Common.Stack != saved.Stack ||
            call.Invocation.Common.ExecutableSha256 != saved.Source.Following.Controls.Main.EngineSha256 ||
            call.Invocation.Common.GameplayOwnership == Guid.Empty || call.Invocation.Caller.Main == Guid.Empty ||
            call.Invocation.Caller.Ordinal < 1 || call.Invocation.Caller.Invocation == Guid.Empty || call.Invocation.Prelude == Guid.Empty ||
            call.Invocation.ThreadId < 1 || call.Entered <= owner.Published ||
            call.Factory != saved.Factory && call.Changed >= current.Published)
            throw new InvalidDataException("Actor-time latest receipt escaped its real source call/publication interval.");
        var fields = call.Before; var scalars = call.BeforeScalars; long last = call.Entered;
        FalloutPlayerLateActorField? previous = null;
        foreach (var store in call.Stores)
        {
            if (!Enum.IsDefined(store.Field) || store.Changed <= last || store.Changed >= call.Changed ||
                previous is { } preceding && ((int)store.Field <= (int)preceding ||
                    preceding == FalloutPlayerLateActorField.RemainingScalar && store.Field == FalloutPlayerLateActorField.ScalarWord))
                throw new InvalidDataException("Actor-time reordered or duplicated committed source stores.");
            var expected = store.Field switch
            {
                FalloutPlayerLateActorField.MobileState => (uint)fields.MobileState,
                FalloutPlayerLateActorField.SelfPredicate => fields.SelfPredicate,
                FalloutPlayerLateActorField.RemainingScalar => scalars.ActorRemainingBits,
                FalloutPlayerLateActorField.ScalarWord => scalars.ActorWord,
                FalloutPlayerLateActorField.DateMismatch => fields.DateMismatch,
                _ => throw new InvalidDataException("Actor-time admitted an unknown field store.")
            };
            if (store.Before != expected) throw new InvalidDataException("Actor-time store replaced its actual preceding field value.");
            switch (store.Field)
            {
                case FalloutPlayerLateActorField.MobileState:
                    if (store.After != byte.MaxValue || fields.MobileState is 0 or 1 or 3)
                        throw new InvalidDataException("Actor-time fabricated a returned nonempty Mobile branch.");
                    fields = fields with { MobileState = byte.MaxValue }; break;
                case FalloutPlayerLateActorField.SelfPredicate:
                    if (store.After != 0) throw new InvalidDataException("Actor-time changed its actual Player-self false store.");
                    fields = fields with { SelfPredicate = 0 }; break;
                case FalloutPlayerLateActorField.RemainingScalar:
                    if (!scalars.SubtractsScaledTimer || call.Timer is not { } timer || store.After !=
                        BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(store.Before) - BitConverter.UInt32BitsToSingle(timer.ScaledSecondsBits)))
                        throw new InvalidDataException("Actor-time clamped or replaced the actual Float32 subtraction.");
                    scalars = scalars with { ActorRemainingBits = store.After }; break;
                case FalloutPlayerLateActorField.ScalarWord:
                    if (scalars.SubtractsScaledTimer || store.After != 0)
                        throw new InvalidDataException("Actor-time substituted a scalar store for the unordered/nonpositive word branch.");
                    scalars = scalars with { ActorWord = 0 }; break;
                case FalloutPlayerLateActorField.DateMismatch:
                    if (call.PackedDay is not { } packed || call.CalendarDay is not { } day || store.After != (packed.Day == day.SignedDay ? 0u : 1u) ||
                        fields.DateMismatch == 0 && store.After == 1)
                        throw new InvalidDataException("Actor-time fabricated an unowned weekday/Extra return.");
                    fields = fields with { DateMismatch = checked((byte)store.After) }; break;
            }
            last = store.Changed; previous = store.Field;
        }
        if (fields != call.After || scalars != call.AfterScalars)
            throw new InvalidDataException("Actor-time fields differ from its real committed partial-store prefix.");
        foreach (var flags in new[] { call.ReferenceFlags, call.ActorValueFlags }.OfType<FalloutReferenceFlagRead>())
            if (flags.Source != FalloutReferenceScriptExtraInitializerSource.Reviewed(
                    FalloutPackageCommandFieldsSource.Reviewed(saved.Source.Following.Controls)).Identity ||
                flags.Actor != saved.Actor || flags.Process != owner.CommonProcess || flags.Epoch != saved.Epoch ||
                flags.Constructor != owner.PlayerConstructor || flags.Factory == Guid.Empty || flags.FieldsFactory == Guid.Empty ||
                flags.Stack != saved.Stack || flags.Sequence < 0)
                throw new InvalidDataException("Actor-time flags borrowed an unrelated reference owner.");
        var lives = call.Lives ?? [];
        if (lives.Count > 3) throw new InvalidDataException("Actor-time added or omitted repeated life queries.");
        for (var index = 0; index < lives.Count; index++)
        {
            var life = lives[index];
            RequireSameProcess(call.Invocation, life.Process);
            if (life.Argument || (life.Raw.Value is not { } raw || life.Raw.Failure is not null
                ? life.Result is not null || call.Returned || index != lives.Count - 1 ||
                    call.FailedAt != new[] { FalloutPlayerLateActorTimeStep.FirstLife,
                        FalloutPlayerLateActorTimeStep.SecondLife, FalloutPlayerLateActorTimeStep.ThirdLife }[index]
                : life.Result != FalloutPlayerFollowingContinuation.LifePredicate(raw, false)))
                throw new InvalidDataException("Actor-time life query changed raw value/argument or predicate.");
        }
        if (call.PackedDay is { } date)
        {
            RequireSameProcess(call.Invocation, date.Common);
            if (date.Day != (int)(date.Word & FalloutPlayerLateActorTimeSource.PackedDayMask))
                throw new InvalidDataException("Actor-time replaced its actual low-nine-bit date getter.");
        }
        if (call.CalendarDay is { } calendar)
        {
            if (!FalloutAdvancementRuntimeReceipt.Digest(calendar.WinningSha256) || calendar.Form.ObjectId == 0 ||
                calendar.SignedDay != FalloutGameTime.ActorFrameSignedDay(calendar.Bits) || call.PackedDay is null ||
                calendar.Owner is not { } calendarOwner || calendarOwner.Source != saved.Source.Identity ||
                calendarOwner.Stack != saved.Stack || calendarOwner.Process != call.Invocation.PlayerProcess ||
                calendarOwner.Factory == Guid.Empty || calendarOwner.Sequence < 1 || calendarOwner.ValueRevision < 0 ||
                calendarOwner.Caller != call.Invocation.Caller || calendarOwner.Constructor != call.Invocation.PlayerConstructor ||
                calendarOwner.CommonProcess != call.Invocation.Common.Process || calendarOwner.Epoch != saved.Epoch ||
                calendarOwner.ThreadId != call.Invocation.ThreadId)
                throw new InvalidDataException("Actor-time replaced its current calendar word or signed-byte conversion.");
        }
        if (call.GlobalAi is { } global && (global.Source != saved.Source.Identity || global.Stack != saved.Stack ||
            global.Process == Guid.Empty || global.Sequence < 1 || global.Revision < 0 || global.Value > 1 || global.Boundary is { Length: 0 }))
            throw new InvalidDataException("Actor-time borrowed a foreign or malformed shared AI domain read.");
        if (call.HardwareThreads is { } hardware && (hardware.Declaration.Name != FalloutPlayerLateActorTimeSource.HardwareThreadsSetting ||
            hardware.Declaration.Collection != FalloutIniCollection.Main || hardware.Declaration.Kind != 'i' ||
            hardware.Number is not { } count || !double.IsFinite(count) || count is < int.MinValue or > int.MaxValue ||
            count != Math.Truncate(count) || string.IsNullOrWhiteSpace(hardware.Origin)))
            throw new InvalidDataException("Actor-time hardware-thread count changed its actual signed INI row.");
        if (call.ActorValueFlags is not null && call.HardwareThreads?.Number != 1)
            throw new InvalidDataException("Actor-time entered values beyond its actual synchronous hardware-thread predicate.");
        if (call.Returned && call.ReferenceFlags is null)
            throw new InvalidDataException("Actor-time returned without its first actual reference flag read.");
        var flagSkipped = call.ReferenceFlags is { } first && (first.Flags & FalloutPlayerLateActorTimeSource.ReferenceNoUpdateFlag) != 0;
        if (flagSkipped && call.ActorUpdate is not null)
            throw new InvalidDataException("Actor-time read Actor update past its earlier reference-flag return.");
        var skipped = call.ReferenceFlags is { } initial && (initial.Flags & FalloutPlayerLateActorTimeSource.ReferenceNoUpdateFlag) != 0 ||
            call.ActorUpdate is { Value: false, Failure: null };
        if (call.Returned && !skipped && (call.ReferenceFlags is null || call.ActorUpdate is not { Value: true, Failure: null } ||
            call.Timer is null || call.ClockKind is null or 4 || call.Travel is null || call.Travel.Require() > 0 ||
            lives.Count != 3 || lives[^1].Result != false || call.HardwareThreads is null ||
            call.Stores.Count is < 3 or > 4 || call.Stores[0].Field != FalloutPlayerLateActorField.MobileState ||
            call.Stores[1].Field != FalloutPlayerLateActorField.SelfPredicate ||
            call.Invocation.Common.Level != FalloutDetectionProcessLevel.High ||
            lives[1].Result == false && call.Before.AiBlocked == 0 && call.GlobalAi is null ||
            call.GlobalAi is { Value: not 0 } || call.GlobalAi?.Boundary is not null ||
            call.HardwareThreads.Number == 1 && (call.ActorValueFlags is null ||
                (call.ActorValueFlags.Flags & FalloutPlayerLateActorTimeSource.ActorConstructionFlag) == 0)))
            throw new InvalidDataException("Actor-time fabricated return across an unowned reached child.");
        if (skipped && (call.Stores.Count != 0 || call.Timer is not null || lives.Count != 0))
            throw new InvalidDataException("Actor-time ran fields or children past its genuine early return.");
    }

    // The component cannot prove an Actor-update publication from a GUID alone.
    // The real parent aggregate must invoke this with its captured shared owner.
    internal static void ValidateSharedAiRead(FalloutPlayerLateActorSnapshot saved, FalloutActorUpdateSnapshot updates)
    {
        Validate(saved);
        if (saved.LastCall?.GlobalAi is not { } read) return;
        if (updates.PlayerFrameGlobalAi is not { } global || read.Source != global.Source || read.Stack != updates.Stack ||
            global.Source != saved.Source.Identity || global.Process != updates.CapturedProcess ||
            global.Publications is not { Count: > 0 } || global.Writers is null || read.Sequence > updates.Sequence)
            throw new InvalidDataException("Actor-time shared AI read has no actual retained source-domain publication.");
        var owners = new Dictionary<Guid, (long Start, long End)>();
        for (var index = 0; index < global.Publications.Count; index++)
        {
            var publication = global.Publications[index];
            if (!owners.TryAdd(publication.Process, (publication.Changed,
                index + 1 < global.Publications.Count ? global.Publications[index + 1].Changed : checked(updates.Sequence + 1))))
                throw new InvalidDataException("Shared AI aggregate repeated a process publication.");
        }
        if (!owners.TryGetValue(read.Process, out var interval) || read.Sequence < interval.Start || read.Sequence >= interval.End)
            throw new InvalidDataException("Actor-time shared AI read escaped its actual live process interval.");
        var prefix = global.Writers.Where(writer => writer.Changed <= read.Sequence).ToArray();
        if (prefix.Length != read.Revision || read.Revision > global.Revision || read.Value != 0 ||
            read.Boundary != prefix.FirstOrDefault()?.Boundary ||
            prefix.Where((writer, index) => writer.Revision != index + 1).Any())
            throw new InvalidDataException("Actor-time shared AI read differs from its actual writer-before-effects prefix.");
    }
}
