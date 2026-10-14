using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceModelDataState
{
    internal static void Validate(FalloutReferenceModelDataSnapshot saved) => ValidateHistory(saved, null);

    internal static void ValidateReads(FalloutReferenceModelDataSnapshot saved, IReadOnlyList<FalloutReferenceModelDataRead> reads)
    {
        ArgumentNullException.ThrowIfNull(saved); ArgumentNullException.ThrowIfNull(reads);
        var pending = new Dictionary<(Guid Factory, long Sequence), List<FalloutReferenceModelDataRead>>();
        foreach (var read in reads)
        {
            if (read is null || read.Source != saved.Source.Identity || read.Stack != saved.Stack || read.Actor != saved.Actor || read.Epoch != saved.Epoch)
                throw new InvalidDataException("Reference data read borrowed another source/Actor field owner.");
            var key = (read.Factory, read.Sequence);
            if (!pending.TryGetValue(key, out var values)) pending.Add(key, values = []);
            values.Add(read);
        }
        ValidateHistory(saved, (owner, sequence, data, fields, readable) =>
        {
            if (!pending.Remove((owner.Factory, sequence), out var values)) return;
            foreach (var read in values)
                if (!readable || read.Process != owner.Process || read.Constructor != owner.Constructor || read.Data != data || read.Fields != fields)
                    throw new InvalidDataException("Reference data read differs from its actual historical cold/field interval.");
        });
        if (pending.Count != 0) throw new InvalidDataException("Reference data read has no actual retained writer/publication boundary.");
    }

    private static void ValidateHistory(FalloutReferenceModelDataSnapshot saved,
        Action<FalloutReferenceModelDataPublication, long, Guid?, FalloutReferenceModelDataValues?, bool>? observe)
    {
        ArgumentNullException.ThrowIfNull(saved); saved.Source.Validate(); saved.Actor.Validate();
        if (string.IsNullOrWhiteSpace(saved.Stack) || saved.Process == Guid.Empty || saved.CommonProcess == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Constructor == Guid.Empty || saved.Epoch < 1 || saved.Sequence < 0 ||
            saved.Calls is null || saved.Publications is not { Count: > 0 } ||
            (saved.Data is null) != (saved.Fields is null) || saved.Data == Guid.Empty ||
            saved.WriterBoundary is { } writer && string.IsNullOrWhiteSpace(writer) ||
            saved.Failure is { } failure && string.IsNullOrWhiteSpace(failure))
            throw new InvalidDataException("Reference data snapshot omitted its actual source/process/field/failed prefix.");
        if ((saved.Fields?.Model is null) != (saved.ModelScene is null) || saved.Fields?.OtherModel is not null)
            throw new InvalidDataException("Reference model omitted its complete logical graph or acquired an unowned second model.");
        if (saved.ModelScene is { } modelScene)
        {
            FalloutActorLogicalScene.Validate(modelScene);
            if (modelScene.Factory != saved.Fields!.Model!.Factory)
                throw new InvalidDataException("Reference model graph belongs to another retained source node factory.");
        }
        var publications = new Dictionary<Guid, FalloutReferenceModelDataPublication>();
        var processes = new HashSet<Guid>(); var constructors = new HashSet<Guid>();
        for (var index = 0; index < saved.Publications.Count; ++index)
        {
            var publication = saved.Publications[index];
            if (publication.Process == Guid.Empty || publication.CommonProcess == Guid.Empty || publication.Factory == Guid.Empty || publication.Constructor == Guid.Empty ||
                !processes.Add(publication.Process) || !constructors.Add(publication.Constructor) ||
                !publications.TryAdd(publication.Factory, publication) || publication.Published < 0 || publication.Published > saved.Sequence ||
                publication.CurrentData == Guid.Empty || publication.PreviousData == Guid.Empty ||
                (index == 0 ? publication.Published != 0 || publication.PreviousProcess is not null || publication.PreviousFactory is not null ||
                    publication.PreviousData is not null || publication.CurrentData is not null :
                    publication.PreviousProcess != saved.Publications[index - 1].Process ||
                    publication.PreviousFactory != saved.Publications[index - 1].Factory ||
                    publication.Published < saved.Publications[index - 1].Published ||
                    (publication.PreviousData is null) != (publication.CurrentData is null) ||
                    publication.CurrentData is not null && publication.CurrentData == publication.PreviousData))
                throw new InvalidDataException("Reference data has a disconnected, reused or replayed cold object publication.");
        }
        var current = saved.Publications[^1];
        if (current.Process != saved.Process || current.CommonProcess != saved.CommonProcess || current.Factory != saved.Factory || current.Constructor != saved.Constructor ||
            saved.PreviousData != current.PreviousData)
            throw new InvalidDataException("Reference data current binding changed its actual cold object/factory.");
        long sequence = 0; var ids = new HashSet<Guid>(); Guid? data = null; FalloutReferenceModelDataValues? fields = null;
        var publicationIndex = 1; var bindingIndex = 0;
        var bindings = saved.ColdBindings ?? [];
        if (bindings.Any(binding => binding is null)) throw new InvalidDataException("Cold model history contains a missing entered child.");
        var ownerIntervals = saved.Publications.Select((row, index) => (row.Factory,
            End: index + 1 < saved.Publications.Count ? saved.Publications[index + 1].Published : saved.Sequence)).ToDictionary(row => row.Factory, row => row.End);
        observe?.Invoke(saved.Publications[0], 0, data, fields, true);
        foreach (var call in saved.Calls)
        {
            while (bindingIndex < bindings.Count && bindings[bindingIndex].Entered < call.Entered) ApplyColdBinding(bindings[bindingIndex++]);
            ApplyPublications();
            if (call is null || call.Invocation is not { } invocation || invocation.Call == Guid.Empty || !ids.Add(invocation.Call) ||
                !publications.TryGetValue(call.Factory, out var owner) || invocation.ReferenceProcess != owner.Process ||
                invocation.ReferenceConstructor != owner.Constructor || invocation.CommonProcess != owner.CommonProcess ||
                invocation.Actor != saved.Actor || invocation.Epoch != saved.Epoch || invocation.ThreadId < 1 ||
                !Enum.IsDefined(invocation.Ingress) || invocation.Ingress == FalloutReferenceModelDataIngress.ColdBinding ||
                call.Entered != sequence + 1 || call.Stores is null ||
                call.Stores.Count > 10 || call.BeforeData != data || call.Returned != (call.Failure is null) ||
                call.Failure is { } error && string.IsNullOrWhiteSpace(error) || call.Entered <= owner.Published ||
                call.Changed > ownerIntervals[call.Factory] || invocation.Ingress != FalloutReferenceModelDataIngress.ModelSetter && call.SuppliedModel is not null)
                throw new InvalidDataException("Reference data call lost its ordered actual source caller/factory or failed prefix.");
            sequence = call.Entered;
            if (invocation.Ingress == FalloutReferenceModelDataIngress.ReferenceConstructor &&
                (call != saved.Calls[0] || call.Stores.Count > 1 || call.Returned && call.Stores.Count != 1 ||
                    call.Stores.Count == 1 && (call.Stores[0].Field != "reference-data" || call.Stores[0].Data is not null)))
                throw new InvalidDataException("Reference data replayed or omitted its original null constructor store.");
            var expected = new List<string>();
            if (invocation.Ingress == FalloutReferenceModelDataIngress.ReferenceConstructor) expected.Add("reference-data");
            else if (invocation.Ingress == FalloutReferenceModelDataIngress.ThreeDLoad ||
                invocation.Ingress == FalloutReferenceModelDataIngress.ModelSetter && call.SuppliedModel is not null)
            {
                if (data is null) expected.AddRange(["reference-data", "first-word", "second-word", "water", "scalar", "third-word"]);
                if (invocation.Ingress == FalloutReferenceModelDataIngress.ModelSetter && fields?.Model != call.SuppliedModel)
                {
                    if (fields?.Model is not null) expected.Add("model");
                    expected.AddRange(["model", "model-retained"]);
                }
            }
            else if (invocation.Ingress == FalloutReferenceModelDataIngress.ModelSetter)
            {
                if (fields?.Model is not null) expected.Add("model");
                if (fields?.OtherModel is not null) expected.Add("other-model");
                if (data is not null) expected.Add("reference-data");
            }
            if (call.Stores.Count > expected.Count || call.Returned && call.Stores.Count != expected.Count ||
                !call.Stores.Select(store => store.Field).SequenceEqual(expected.Take(call.Stores.Count)))
                throw new InvalidDataException("Reference data changed its constructor/allocation/setter committed-store prefix.");
            foreach (var store in call.Stores)
            {
                if (store.Changed != ++sequence) throw new InvalidDataException("Reference data reordered its committed source stores.");
                switch (store.Field)
                {
                    case "reference-data":
                        if (store.Bits is not null || store.Node is not null || store.Data == Guid.Empty || data is not null && store.Data is not null)
                            throw new InvalidDataException("Reference data replaced or invented an allocated source pointer.");
                        if ((invocation.Ingress == FalloutReferenceModelDataIngress.ThreeDLoad ||
                            invocation.Ingress == FalloutReferenceModelDataIngress.ModelSetter && call.SuppliedModel is not null) && store.Data is null)
                            throw new InvalidDataException("Reference data replaced its real allocation with a null publication.");
                        data = store.Data; fields = data is null ? null : new(null, null, null, null, null, null, null); break;
                    case "first-word": fields = NeedFields() with { FirstWord = Zero() }; break;
                    case "second-word": fields = NeedFields() with { SecondWord = Zero() }; break;
                    case "water":
                        if (store.Bits != FalloutPlayerMoverDeltaSource.UninitializedWater)
                            throw new InvalidDataException("Reference data admitted an unowned current CELL-water payload.");
                        fields = NeedFields() with { WaterBits = store.Bits }; break;
                    case "scalar":
                        if (store.Bits != FalloutPlayerMoverDeltaSource.UninitializedScalar)
                            throw new InvalidDataException("Reference data changed its independent constructor scalar.");
                        fields = NeedFields() with { ScalarBits = store.Bits }; break;
                    case "third-word": fields = NeedFields() with { ThirdWord = Zero() }; break;
                    case "model": Node(store.Node); fields = NeedFields() with { Model = store.Node }; break;
                    case "other-model": Node(store.Node); fields = NeedFields() with { OtherModel = store.Node }; break;
                    case "model-retained":
                        if (NeedFields().Model != store.Node || store.Node is null || store.Node != call.SuppliedModel)
                            throw new InvalidDataException("Reference model acquire omitted its actual preceding supplied pointer store.");
                        Node(store.Node); break;
                    default: throw new InvalidDataException("Reference data contains an unowned field writer.");
                }
                uint Zero() => store.Bits == 0 && store.Data is null && store.Node is null ? 0u :
                    throw new InvalidDataException("Reference data changed its original scalar zero.");
                void Node(FalloutSourceSceneNode? node)
                {
                    if (invocation.Ingress != FalloutReferenceModelDataIngress.ModelSetter || store.Bits is not null || store.Data is not null ||
                        node is not null && (node.Process != owner.Process || node.Factory == Guid.Empty || node.NativeRoot == 0))
                        throw new InvalidDataException("Reference model writer omitted its actual current returned node/native factory.");
                }
            }
            if (call.AfterData != data || call.Changed != ++sequence)
                throw new InvalidDataException("Reference data current pointer disagrees with its committed call prefix.");
            observe?.Invoke(owner, sequence, data, fields, call.Returned && invocation.Ingress != FalloutReferenceModelDataIngress.UnownedWriter &&
                (fields?.Model is null || fields.Model.Process == owner.Process));
            FalloutReferenceModelDataValues NeedFields() => fields ?? throw new InvalidDataException("Reference data wrote a field before allocating its actual object.");
        }
        while (bindingIndex < bindings.Count) ApplyColdBinding(bindings[bindingIndex++]);
        ApplyPublications();
        var failedCall = saved.Calls.FirstOrDefault(call => !call.Returned);
        var failedBinding = bindings.FirstOrDefault(binding => !binding.Returned);
        var firstFailure = failedCall is null ? failedBinding?.Failure :
            failedBinding is null || failedCall.Entered < failedBinding.Entered ? failedCall.Failure : failedBinding.Failure;
        if (saved.ModelScene is { } graph && bindings.LastOrDefault(binding => binding.ModelStored && binding.SuppliedModel == saved.Fields!.Model) is { } currentBinding &&
            (graph.Lifetime != currentBinding.LogicalLifetime || graph.RestoredLifetime != currentBinding.PreviousScene.Lifetime ||
                graph.Sequence < currentBinding.LogicalSequence || graph.Root != currentBinding.PreviousScene.Root ||
                System.Text.Json.JsonSerializer.Serialize(graph.Assets) != System.Text.Json.JsonSerializer.Serialize(currentBinding.PreviousScene.Assets)))
            throw new InvalidDataException("Current reference graph lost its actual cold logical lifetime/resource binding.");
        if (publicationIndex != saved.Publications.Count || saved.Sequence != sequence || saved.Data != data || saved.Fields != fields ||
            saved.Failure != firstFailure ||
            saved.WriterBoundary is not null && !saved.Calls.Any(call => call.Invocation.Ingress == FalloutReferenceModelDataIngress.UnownedWriter))
            throw new InvalidDataException("Reference data current/cold state differs from its genuine ordered field histories.");

        void ApplyPublications()
        {
            while (publicationIndex < saved.Publications.Count && saved.Publications[publicationIndex].Published == sequence)
            {
                var publication = saved.Publications[publicationIndex++];
                if (publication.PreviousData != data) throw new InvalidDataException("Cold reference data rebound another saved pointer.");
                data = publication.CurrentData;
                observe?.Invoke(publication, sequence, data, fields, fields?.Model is null);
            }
        }
        void ApplyColdBinding(FalloutReferenceModelColdBinding binding)
        {
            ApplyPublications();
            if (binding is null || binding.PreviousScene is null || binding.PreviousModel is null || binding.SuppliedModel is null ||
                binding.Invocation is not { } invocation || invocation.Ingress != FalloutReferenceModelDataIngress.ColdBinding ||
                invocation.Call == Guid.Empty || !ids.Add(invocation.Call) || !publications.TryGetValue(binding.Factory, out var owner) ||
                owner.PreviousProcess is null || invocation.ReferenceProcess != owner.Process || invocation.ReferenceConstructor != owner.Constructor ||
                invocation.CommonProcess != owner.CommonProcess || invocation.Actor != saved.Actor || invocation.Epoch != saved.Epoch || invocation.ThreadId < 1 ||
                binding.Data == Guid.Empty || binding.Data != data || binding.Entered != sequence + 1 || binding.Entered <= owner.Published ||
                binding.Changed != binding.Entered + 1 || binding.Changed > ownerIntervals[binding.Factory] ||
                fields?.Model != binding.PreviousModel || binding.PreviousModel.Factory != binding.PreviousScene.Factory ||
                binding.SuppliedModel.Process != owner.Process || binding.SuppliedModel.Factory == Guid.Empty || binding.SuppliedModel.NativeRoot == 0 ||
                binding.SuppliedModel.Factory == binding.PreviousModel.Factory || binding.Returned != (binding.Failure is null) ||
                binding.Failure is { } failure && string.IsNullOrWhiteSpace(failure) ||
                binding.ModelRetained && !binding.ModelStored || binding.Returned && (!binding.ModelStored || !binding.ModelRetained) ||
                (binding.LogicalLifetime is null) != (binding.LogicalSequence is null) || binding.LogicalLifetime == Guid.Empty ||
                binding.LogicalLifetime is not null && (binding.LogicalLifetime == binding.PreviousScene.Lifetime ||
                    binding.LogicalSequence != binding.PreviousScene.Sequence + 1) ||
                binding.ModelStored && (binding.LogicalLifetime is null || binding.LogicalLifetime == Guid.Empty ||
                    binding.LogicalLifetime == binding.PreviousScene.Lifetime || binding.LogicalSequence != binding.PreviousScene.Sequence + 1))
                throw new InvalidDataException("Cold model lost its actual saved field, fresh graph binding, source caller or failed prefix.");
            FalloutActorLogicalScene.RequireComplete(binding.PreviousScene);
            if (binding.ModelStored) fields = fields! with { Model = binding.SuppliedModel };
            sequence = binding.Changed;
            observe?.Invoke(owner, sequence, data, fields, binding.Returned);
        }
    }
}
