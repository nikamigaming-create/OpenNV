using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollBodyPartVisit(byte Part, byte Flags, string IkStartNode);
internal sealed record FalloutRagdollBodyPartPreparation(FalloutReferenceModelDataInvocation Load,
    FalloutFormKey Source, string SourceSha256, IReadOnlyList<FalloutRagdollBodyPartVisit> Visits,
    IReadOnlyList<string> IkNodes, byte? TrackingPart, bool Enumerated, string? Failure, string? EditorId = null, bool? Prime = null)
{
    internal const string PrimeEditorSelector = "Prime";
    internal void Validate(FalloutLivingModelSnapshot model)
    {
        if (Load is null || !FalloutAdvancementRuntimeReceipt.Digest(SourceSha256) || Visits is null || IkNodes is null || Visits.Count > 15 ||
            model.Controller is not { Returned: true } controller || controller.Load != Load ||
            Visits.Any(visit => visit is null || visit.Part >= 15 || (visit.Flags & 2) == 0 || visit.IkStartNode is null) ||
            !Visits.Select(visit => visit.Part).SequenceEqual(Visits.Select(visit => visit.Part).Distinct().OrderDescending()) ||
            !IkNodes.SequenceEqual(Visits.Where(visit => (visit.Flags & 4) != 0).Select(visit => visit.IkStartNode)) ||
            TrackingPart != Visits.FirstOrDefault(visit => (visit.Flags & 0x20) != 0)?.Part ||
            Enumerated && Failure is not null || Failure is not null && string.IsNullOrWhiteSpace(Failure) ||
            (EditorId is null) != (Prime is null) || EditorId is not null &&
                (!Enumerated || Prime != EditorId.Contains(PrimeEditorSelector, StringComparison.Ordinal)))
            throw new InvalidDataException("Ragdoll body-part preparation changed its real source/controller or selected list prefix.");
    }
}

internal sealed partial class FalloutLivingActorModel
{
    private FalloutRagdollBodyPartPreparation? _ragdollBodyParts;
    private bool _ragdollBodyPartsEntered;
    private long _ragdollBodyPartsReentry;
    internal FalloutRagdollBodyPartPreparation? CaptureRagdollBodyParts() => _ragdollBodyParts is { } state ?
        state with { Visits = state.Visits.ToArray(), IkNodes = state.IkNodes.ToArray() } : null;

    internal bool ClassifyRagdollBodyPartModel(FalloutReferenceModelDataInvocation load, FalloutPluginRecord record,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load); _ragdollController?.RequireReturned();
        if (_ragdollBodyParts is not { Enumerated: true, Failure: null, EditorId: null } prepared || prepared.Load != load || prepared.Source != record.FormKey ||
            prepared.SourceSha256 != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record.ReadData())).ToLowerInvariant())
            throw new InvalidOperationException("Ragdoll body-part classification lost its actual enumerated source or replayed.");
        var fields = record.ReadSubrecords().Where(field => field.Signature == "EDID").ToArray();
        if (fields.Length > 1) throw new InvalidDataException("Ragdoll body-part source repeats its editor-name field.");
        var editor = fields.Length == 0 ? "" : FalloutDialogueTopic.Text(fields[0].Data.Span);
        var prime = editor.Contains(FalloutRagdollBodyPartPreparation.PrimeEditorSelector, StringComparison.Ordinal);
        _ragdollBodyParts = prepared with { EditorId = editor, Prime = prime };
        requireActual(load); return prime;
    }

    internal IReadOnlyList<string> PrepareRagdollBodyParts(FalloutReferenceModelDataInvocation load, FalloutPluginRecord record,
        Action<FalloutBodyPart> initializeTracking, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        if (_ragdollBodyPartsEntered)
        { ++_ragdollBodyPartsReentry; throw new InvalidOperationException("Ragdoll body-part preparation reentered its actual source call."); }
        RequireCurrent(); requireActual(load); _ragdollController?.RequireReturned();
        if (_ragdollController is null || _ragdollBodyParts is not null || _ragdollController.Capture() is not { ModelAxes: not null } controller || controller.Load != load)
            throw new InvalidOperationException("Ragdoll body-part preparation lost its same returned/configured controller or replayed.");
        var visits = new List<FalloutRagdollBodyPartVisit>(); var nodes = new List<string>();
        _ragdollBodyParts = new(load, record.FormKey, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record.ReadData())).ToLowerInvariant(),
            visits.AsReadOnly(), nodes.AsReadOnly(), null, false, null);
        var fault = _ragdollBodyPartsReentry; _ragdollBodyPartsEntered = true;
        try
        {
            var table = FalloutBodyPartData.Read(record);
            // The source walks the fifteen slots in ascending order and
            // pushes eligible parts at the list head. The consumer therefore
            // receives descending slot order, independently of file order.
            foreach (var part in table.EnumerateRagdollParts())
            {
                Check(); visits.Add(new(part.Type, part.Flags, part.IkStartNode));
                if ((part.Flags & 4) != 0) nodes.Add(part.IkStartNode);
                if (_ragdollBodyParts.TrackingPart is null && (part.Flags & 0x20) != 0)
                {
                    _ragdollBodyParts = _ragdollBodyParts with { TrackingPart = part.Type };
                    initializeTracking(part); Check();
                }
            }
            _ragdollBodyParts = _ragdollBodyParts with { Enumerated = true };
            return nodes.AsReadOnly();
        }
        catch (Exception error) { _ragdollBodyParts = _ragdollBodyParts with { Failure = error.Message }; throw; }
        finally { _ragdollBodyPartsEntered = false; }
        void Check()
        {
            RequireCurrent(); requireActual(load); _ragdollController.RequireReturned();
            if (_ragdollBodyPartsReentry != fault) throw new InvalidOperationException("Ragdoll body-part preparation swallowed source reentry.");
        }
    }
}
