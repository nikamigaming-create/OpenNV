using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferencePackageOwnerValidation
{
    // Invoke after BuildFlagIndex has indexed all Common/scene publications.
    // The existing current Parent.Process == Runtime check remains in place.
    private void ValidateParentRuntimePublications(FalloutActorProcessRuntimeSnapshot? runtime)
    {
        if (_parent is null) return;
        _parentValidation ??= new(_parent);
        if (_parent.Publications.Count == 1)
        {
            if (!_initializerPublications.Values.Any(value => value.Constructor == _parent.Constructor))
                throw new InvalidDataException("ParentCELL source construction lost its actual scene/Common publication.");
            return;
        }
        var reference = _common.ReferenceOwners ??
            throw new NotSupportedException("source-cold-reference-native-factory-history-unbound");
        if (runtime is null || reference.Position.Actor != _position.Actor || reference.Position.Factory != _position.Factory ||
            reference.Position.Process != _position.Process || reference.Parent != _parent ||
            reference.EarlyController.Source != FalloutEarlyControllerSource.Reviewed(_position.Source.Fields,
                FalloutPlayerThreadContextSource.Reviewed(_position.Source.Fields.Controls.Main)).Identity)
            throw new InvalidDataException("Cold Parent publications changed their actual aggregate reference/native sources.");
        var source = FalloutEarlyControllerSource.Reviewed(_position.Source.Fields,
            FalloutPlayerThreadContextSource.Reviewed(_position.Source.Fields.Controls.Main));
        var inputs = new FalloutPhysicalConstructorInputs(source.Fields.Controls.Main.EngineSha256,
            FalloutPhysicalEffectiveSettings.Read(name => FalloutGameSettingFloats.ReadRetained(_records, name,
                nameof(FalloutReferencePackageOwnerValidation))));
        FalloutEarlyControllerFactory.ValidateColdFactory(reference.EarlyController, source, inputs);
        var publications = new HashSet<(Guid Runtime, Guid Constructor, Guid Common, Guid Thread)>();
        foreach (var controller in reference.EarlyController.Controllers)
        {
            var lineage = controller.Lineage;
            if (lineage.Actor == _position.Actor) publications.Add((lineage.RuntimeProcess, lineage.Constructor,
                lineage.CommonProcess, lineage.ThreadProcess));
        }
        foreach (var publication in _parent.Publications)
        {
            var joined = publications.Where(value => value.Runtime == publication.Process && value.Constructor == publication.Constructor).ToArray();
            if (joined.Length != 1 || !_initializerPublications.TryGetValue(joined[0].Common, out var initializer) ||
                initializer.Constructor != publication.Constructor || initializer.ThreadProcess != joined[0].Thread)
                throw new InvalidDataException("ParentCELL retained a foreign historical Runtime/scene/controller publication.");
        }
        if (_parent.Process != runtime.CapturedProcess ||
            !publications.Contains((runtime.CapturedProcess, _position.Constructor, _common.CapturedProcess, _position.Process)))
            throw new InvalidDataException("Cold Parent publication lost its fresh actual Runtime/Common/reference-controller join.");
    }
}
