using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSkyPrecipitationFactory(FalloutSkyNativeChildSource Source, FalloutIniValue Setting);
internal sealed record FalloutSkyPrecipitationSnapshot(bool FirstNodeConstructorNull, bool SecondNodeConstructorNull,
    uint ScalarBits, bool ParentBound);

internal sealed partial class FalloutSkyTransferState
{
    private readonly Dictionary<Guid, IFalloutSkyResetChild> _failedNativeFactories = [];
    internal FalloutSkyChildSnapshot? RetainedCloudFields
    {
        get { RequireLiving(); return _cloudBinding.Source; }
    }
    internal void RetainFailedNativeFactory(IFalloutSkyResetChild child, Exception error)
    {
        if (child.Identity == Guid.Empty || child.Sky != Identity || child.Role is not ("Clouds" or "Precipitation") ||
            _thread is { } thread && thread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("A foreign native factory reported source Sky resource retention.", error);
        _failedNativeFactories[child.Identity] = child;
        _retirementFailure = error.ToString(); Next();
    }

    internal void BindPrecipitationFactory(FalloutSkyPrecipitationFactory factory)
    {
        RequireWriter(); factory.Source.Require(Source);
        if (factory.Setting.Declaration.Collection != FalloutIniCollection.Main ||
            factory.Setting.Declaration.Name != FalloutSkyNativeChildSource.PrecipitationSetting ||
            factory.Setting.Declaration.Kind != 'b' || factory.Setting.Number is not (0 or 1) ||
            string.IsNullOrWhiteSpace(factory.Setting.Origin))
            throw new InvalidDataException("Precipitation has no exact selected source gate.");
        if (_precipitation is not null || _precipitationBinding.Factory is { } old && old != factory)
            throw new InvalidOperationException("Precipitation factory would replace another living or cold source gate.");
        if (factory.Setting.Number == 0)
        {
            if (_precipitationBinding.Disposition == FalloutSkyChildDisposition.Published)
                throw new InvalidDataException("Cold Precipitation changed an enabled constructor into a disabled source field.");
            _precipitationBinding = new(FalloutSkyChildDisposition.ConstructorNull, null, null, factory);
        }
        else _precipitationBinding = _precipitationBinding with
        {
            Disposition = _precipitationBinding.Disposition == FalloutSkyChildDisposition.Published ?
                FalloutSkyChildDisposition.Published : FalloutSkyChildDisposition.Unowned,
            Failure = _precipitationBinding.Disposition == FalloutSkyChildDisposition.Published ? null :
                "source-Precipitation-enabled-factory-native-parent-publication-pending",
            Factory = factory,
        };
        Next();
    }
    private static void ValidatePrecipitationBinding(FalloutSkyChildBinding child, FalloutSkyTransferDeclaration source)
    {
        if (child.Factory is not { } factory)
        {
            if (child.Disposition == FalloutSkyChildDisposition.Published)
                throw new InvalidDataException("Saved Precipitation publication omitted its actual constructor gate.");
            return; // A source Sky constructor precedes the optional factory.
        }
        factory.Source.Require(source);
        if (factory.Setting.Declaration.Collection != FalloutIniCollection.Main ||
            factory.Setting.Declaration.Name != FalloutSkyNativeChildSource.PrecipitationSetting ||
            factory.Setting.Declaration.Kind != 'b' || factory.Setting.Number is not (0 or 1) ||
            string.IsNullOrWhiteSpace(factory.Setting.Origin) ||
            factory.Setting.Number == 0 && child.Disposition != FalloutSkyChildDisposition.ConstructorNull ||
            factory.Setting.Number == 1 && child.Disposition == FalloutSkyChildDisposition.ConstructorNull)
            throw new InvalidDataException("Saved Precipitation changed its independent actual source gate.");
        if (child.Disposition == FalloutSkyChildDisposition.Published &&
            (child.Source is not { Role: "Precipitation", Precipitation: { } nodes } published ||
             published.SourceSha256 != factory.Source.Contract || published.Resource != "source-Precipitation-field" ||
             published.CloudSlots.Count != 0 || !nodes.FirstNodeConstructorNull || !nodes.SecondNodeConstructorNull ||
             !nodes.ParentBound || nodes.ScalarBits != 0))
            throw new InvalidDataException("Saved Precipitation introduced an unowned Rain/Snow node or parent/scalar writer.");
    }
}
