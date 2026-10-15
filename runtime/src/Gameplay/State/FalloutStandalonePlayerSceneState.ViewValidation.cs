using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal static void ValidateViewFields(FalloutStandalonePlayerSceneSnapshot saved)
    {
        var fields = saved.ViewFields ?? throw new InvalidDataException("Player view has no actual constructor or source writer.");
        if (fields.Source != saved.Source.Identity || fields.Stack != saved.Stack || fields.Process != saved.CapturedProcess ||
            fields.Constructor != saved.Constructor || fields.Value != saved.PlayerView || fields.Sequence != saved.Sequence ||
            fields.Publications is not { Count: > 0 } || saved.PointOfView?.Lifetimes is not { } actualLifetimes ||
            actualLifetimes.Count != fields.Publications.Count)
            throw new InvalidDataException("Player view lost its SAME Scene source/current field.");
        var publications = new Dictionary<Guid, (FalloutPlayerViewPublication Row, long End)>();
        var constructors = new HashSet<Guid>();
        for (var index = 0; index < fields.Publications.Count; index++)
        {
            var row = fields.Publications[index];
            var end = index + 1 < fields.Publications.Count ? fields.Publications[index + 1].Published - 1 : saved.Sequence;
            var actualLifetime = actualLifetimes[index];
            if (row.Process == Guid.Empty || row.Constructor == Guid.Empty || row.Published < 0 || row.Published > end ||
                end > saved.Sequence || !constructors.Add(row.Constructor) || !publications.TryAdd(row.Process, (row, end)) ||
                actualLifetime.Process != row.Process || actualLifetime.Constructor != row.Constructor || actualLifetime.Published != row.Published ||
                (index == 0 ? row.Published != 0 || row.Value != 0 || row.PreviousProcess is not null || row.PreviousConstructor is not null :
                    row.PreviousProcess != fields.Publications[index - 1].Process ||
                    row.PreviousConstructor != fields.Publications[index - 1].Constructor))
                throw new InvalidDataException("Player view replaced or disconnected its actual cold process publications.");
        }
        var current = fields.Publications[^1];
        if (current.Process != saved.CapturedProcess || current.Constructor != saved.Constructor ||
            saved.ColdHandoff is { } cold && (current.Process != cold.CurrentProcess || current.PreviousProcess != cold.PreviousProcess ||
                current.Constructor != cold.CurrentConstructor || current.PreviousConstructor != cold.PreviousConstructor || current.Published != cold.Changed))
            throw new InvalidDataException("Player view current publication changed the actual Scene cold handoff.");

        var stores = new List<FalloutPlayerViewStore>(4);
        if (fields.Load is { } load) ValidateSpan(load, isLoad: true);
        if (fields.Tail is { } tail) ValidateSpan(tail, isLoad: false);
        var last = stores.OrderBy(store => store.Changed).LastOrDefault();
        if (fields.LastWrite != last || fields.Value != (last?.After ?? current.Value) ||
            stores.Select(store => store.Changed).Distinct().Count() != stores.Count)
            throw new InvalidDataException("Player view omitted its latest real store or substituted the current byte.");
        if (last is not null)
            foreach (var row in fields.Publications.Where(row => row.Published >= last.Changed))
                if (row.Value != last.After)
                    throw new InvalidDataException("Cold view publication changed retained state without a source writer.");

        void ValidateSpan(FalloutPlayerViewSpan span, bool isLoad)
        {
            ValidateRead(span.Initial, isLoad);
            ValidateStore(span.First, isLoad ? FalloutPlayerViewWriter.LoadTemporary : FalloutPlayerViewWriter.FirstTailToggle);
            if (!SameScope(span.Initial, span.First.Read) || span.First.Read.Sequence < span.Initial.Sequence ||
                isLoad && span.First.Read != span.Initial || !isLoad && span.Initial.Value == 0 && span.First.Read != span.Initial ||
                span.First.After != (isLoad ? 1 : Toggle(span.First.Read.Value)))
                throw new InvalidDataException("Player view changed the real initial read or first source store law.");
            if (span.Second is not { } second) return;
            ValidateStore(second, isLoad ? FalloutPlayerViewWriter.LoadRestore : FalloutPlayerViewWriter.SecondTailToggle);
            if (!SameScope(span.Initial, second.Read) || second.Consumer != span.First.Consumer ||
                second.Read.Sequence < span.First.Changed || second.Changed <= span.First.Changed ||
                second.After != (isLoad ? span.Initial.Value : Toggle(second.Read.Value)))
                throw new InvalidDataException("Player view lost its exact saved restore value or fresh second toggle.");
        }
        void ValidateStore(FalloutPlayerViewStore store, FalloutPlayerViewWriter writer)
        {
            ValidateRead(store.Read, writer is FalloutPlayerViewWriter.LoadTemporary or FalloutPlayerViewWriter.LoadRestore);
            var owner = publications[store.Read.Process];
            if (store.Source != fields.Source || store.Consumer == Guid.Empty || store.Writer != writer ||
                store.Changed <= store.Read.Sequence || store.Changed > owner.End ||
                store.Read.Load is { } load && store.Consumer != load.Call)
                throw new InvalidDataException("Player view store replaced its actual writer/source/interval.");
            stores.Add(store);
        }
        void ValidateRead(FalloutPlayerViewRead read, bool isLoad)
        {
            if (read.Source != fields.Source || read.Stack != fields.Stack ||
                !publications.TryGetValue(read.Process, out var owner) || read.Constructor != owner.Row.Constructor ||
                read.Sequence < owner.Row.Published || read.Sequence > owner.End ||
                (read.Load is not null) != isLoad || (read.Frame is not null) == isLoad)
                throw new InvalidDataException("Player view read lost its actual source process interval.");
            if (read.Load is { } load && (load.Call == Guid.Empty || load.ReferenceProcess != read.Process ||
                load.ReferenceConstructor != read.Constructor || load.CommonProcess == Guid.Empty || !load.Actor.EnginePlayer ||
                load.Epoch < 1 || load.ThreadId < 1 || load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad) ||
                read.Frame is { } frame && (frame.PlayerProcess != read.Process || frame.PlayerConstructor != read.Constructor ||
                    frame.Prelude == Guid.Empty || frame.Caller.Main == Guid.Empty || frame.Caller.Invocation == Guid.Empty ||
                    frame.Caller.Ordinal < 1 || frame.ThreadId < 1 || frame.Common.Process == Guid.Empty || !frame.Common.Actor.EnginePlayer ||
                    frame.Common.Stack != saved.Stack || frame.Common.Epoch < 1 ||
                    frame.Common.ExecutableSha256 != saved.Source.Main.EngineSha256))
                throw new InvalidDataException("Player view substituted a source load or entered frame.");
        }
        static bool SameScope(FalloutPlayerViewRead first, FalloutPlayerViewRead second) =>
            first.Source == second.Source && first.Stack == second.Stack && first.Process == second.Process &&
            first.Constructor == second.Constructor && first.Load == second.Load && first.Frame == second.Frame;
    }
}
