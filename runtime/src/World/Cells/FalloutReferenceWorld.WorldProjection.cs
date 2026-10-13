using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceWorldProjectionSnapshot(string Schema,
    FalloutWorldProjectionPublicationSnapshot Publication, FalloutWorldProjectionSnapshot? UnpublishedConstructor,
    string? ConstructionFailure)
{
    internal const string CurrentSchema = "opennv-source-world-projection-publication/v1";
    internal void Validate()
    {
        if (Schema != CurrentSchema || Publication is null || !FalloutAdvancementRuntimeReceipt.Digest(Publication.Source) ||
            Publication.CapturedWorld == Guid.Empty || Publication.Factory == Guid.Empty ||
            Publication.Replacements < 0 || string.IsNullOrWhiteSpace(Publication.Stack) ||
            (Publication.Current is null) != (Publication.CurrentRootFactory is null))
            throw new InvalidDataException("World projection cold state lost its actual publication owner.");
        if (Publication.Current is { } current)
        {
            FalloutWorldProjectionOwner.Validate(current);
            if (!current.ConstructorReturned || current.Retired || current.Factory != Publication.CurrentRootFactory ||
                current.CapturedWorld != Publication.CapturedWorld || current.Source.Identity != Publication.Source ||
                current.Stack != Publication.Stack || Publication.Replacements < 1 || UnpublishedConstructor is not null ||
                ConstructionFailure is not null)
                throw new InvalidDataException("World projection cold publication selected an incomplete or foreign returned root.");
        }
        if (UnpublishedConstructor is { } failed)
        {
            FalloutWorldProjectionOwner.Validate(failed);
            if (Publication.Current is not null || Publication.Replacements != 0 || failed.ConstructorReturned || failed.Retired ||
                failed.CapturedWorld != Publication.CapturedWorld || failed.Source.Identity != Publication.Source ||
                failed.Stack != Publication.Stack ||
                (failed.Failure is not null ? ConstructionFailure != failed.Failure : failed.ConstructorEntered) ||
                string.IsNullOrWhiteSpace(ConstructionFailure))
                throw new InvalidDataException("World projection cold state manufactured publication from a failed constructor.");
        }
        else if (ConstructionFailure is not null)
            throw new InvalidDataException("World projection cold failure has no retained actual constructor child.");
    }
}

internal sealed partial class FalloutReferenceWorld
{
    private readonly Guid _standaloneWorldProjectionIdentity = Guid.NewGuid();
    private FalloutPlayerWorldOrientationSource? _standaloneWorldOrientationSource;
    private FalloutWorldProjectionPublication? _standaloneWorldProjection;
    private FalloutWorldProjectionOwner? _standaloneWorldProjectionAttempt;
    private string? _standaloneWorldProjectionConstructionFailure;
    private bool _standaloneWorldProjectionRetired;
    private FalloutReferenceWorldProjectionSnapshot? _standaloneWorldProjectionRestore;
    private bool _standaloneWorldProjectionRestorePrepared;

    internal void PrepareStandaloneWorldProjectionRestore(FalloutReferenceWorldProjectionSnapshot? saved)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_standaloneWorldProjection is not null || _standaloneWorldProjectionRetired)
            throw new InvalidOperationException("Cold World projection must be prepared before actual publication.");
        if (_standaloneWorldProjectionRestorePrepared)
        {
            if (!ReferenceEquals(saved, _standaloneWorldProjectionRestore))
                throw new InvalidOperationException("World projection changed its prepared actual cold state.");
            return;
        }
        if (!HasStandalonePlayerScene || (_processRuntimeRestore is null) != (saved is null))
            throw new InvalidDataException("Cold standalone World cannot omit its projection or replay a fresh source constructor.");
        if (saved is not null)
        {
            saved.Validate(); var source = PrepareStandaloneWorldOrientationSource();
            if (saved.Publication.Source != source.Identity || saved.Publication.Stack != CampaignStandalonePlayerScene.Stack ||
                saved.Publication.CapturedWorld == _standaloneWorldProjectionIdentity)
                throw new InvalidDataException("Prepared projection changed its selected source/stack or reused its historical World.");
        }
        _standaloneWorldProjectionRestore = saved; _standaloneWorldProjectionRestorePrepared = true;
    }

    internal FalloutReferenceWorldProjectionSnapshot? PreparedStandaloneWorldProjectionRestore
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_standaloneWorldProjectionRestorePrepared && _processRuntimeRestore is not null)
                throw new InvalidDataException("Cold standalone World has no prepared projection snapshot.");
            return _standaloneWorldProjectionRestore;
        }
    }

    internal string? StandaloneWorldProjectionSaveBlocker => !HasStandalonePlayerScene ? null :
        _standaloneWorldProjectionRetired ? "source-current-World-projection-retired" :
        _standaloneWorldProjectionConstructionFailure ?? _standaloneWorldProjection?.SaveBlocker ??
        (_standaloneWorldProjection is null ? "source-current-World-root-publication-unowned" : null);

    // Diagnostic state reads current fields directly. Validation/capture failure
    // must not erase the stores that preceded a failing constructor or child.
    internal object StandaloneWorldProjectionState => new
    {
        source = _standaloneWorldOrientationSource?.Identity,
        world = _standaloneWorldProjectionIdentity,
        coldPrepared = _standaloneWorldProjectionRestorePrepared,
        coldWorld = _standaloneWorldProjectionRestore?.Publication.CapturedWorld,
        publication = _standaloneWorldProjection?.State,
        constructor = _standaloneWorldProjectionAttempt?.State,
        failure = _standaloneWorldProjectionConstructionFailure,
        retired = _standaloneWorldProjectionRetired,
        saveBlocker = StandaloneWorldProjectionSaveBlocker
    };

    // Source reading can happen during preparation. Mutable World/camera owners
    // are created only by ConstructStandaloneWorldProjection on publication.
    internal FalloutPlayerWorldOrientationSource PrepareStandaloneWorldOrientationSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasStandalonePlayerScene) throw new NotSupportedException("World projection has no selected actual standalone Player.");
        var controls = FalloutPlayerMovementControlSource.Reviewed(CampaignStandalonePlayerScene.Source.Main);
        if (_standaloneWorldOrientationSource is { } source)
        {
            if (source.Controls != controls) throw new InvalidOperationException("World projection changed its selected actual Main/control source.");
            return source;
        }
        var owned = CampaignPlayerRuntimeSource.OwnedSource;
        return _standaloneWorldOrientationSource = FalloutExecutableStringTable.ReadPlayerWorldOrientation(owned.FalloutExecutablePath, controls);
    }

    internal Guid StandaloneWorldProjectionIdentity
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _standaloneWorldProjectionIdentity; }
    }
    internal FalloutWorldProjectionPublication CampaignStandaloneWorldProjection
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var publication = _standaloneWorldProjection ?? throw new NotSupportedException("source-current-World-root-publication-unowned");
            publication.RequireCurrent(); return publication;
        }
    }

    internal FalloutWorldProjectionPublication ConstructStandaloneWorldProjection(Func<FalloutWorldProjectionTargetRead> readCurrentTarget,
        Func<bool> actualPublicationLiving, FalloutReferenceWorldProjectionSnapshot? saved = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(readCurrentTarget);
        ArgumentNullException.ThrowIfNull(actualPublicationLiving);
        if (_standaloneWorldProjection is not null || _standaloneWorldProjectionRetired)
            throw new InvalidOperationException("World projection construction cannot be replaced or retried within its actual world lifetime.");
        if (!_standaloneWorldProjectionRestorePrepared) PrepareStandaloneWorldProjectionRestore(saved);
        else if (!ReferenceEquals(saved, _standaloneWorldProjectionRestore))
            throw new InvalidDataException("World projection construction did not consume its exact prepared cold snapshot.");
        var source = PrepareStandaloneWorldOrientationSource(); var stack = CampaignStandalonePlayerScene.Stack;
        bool Living() => !_disposed && !_standaloneWorldProjectionRetired && actualPublicationLiving();
        if (!Living()) throw new InvalidOperationException("World projection was constructed outside its actual gameplay publication.");
        if (saved is not null)
        {
            saved.Validate();
            if (saved.Publication.Source != source.Identity || saved.Publication.Stack != stack ||
                saved.Publication.CapturedWorld == _standaloneWorldProjectionIdentity)
                throw new InvalidDataException("Cold World projection changed its selected source or reused its historical world.");
        }
        var publication = new FalloutWorldProjectionPublication(source, stack, _standaloneWorldProjectionIdentity, Living);
        _standaloneWorldProjection = publication;
        var retained = saved?.Publication.Current ?? saved?.UnpublishedConstructor;
        _standaloneWorldProjectionAttempt = saved is null || retained is not null ?
            new(source, stack, _standaloneWorldProjectionIdentity, Living, retained) : null;
        if (saved is not null)
        {
            // Restore binds fresh objects without invoking source construction,
            // target queries, placement, projection or reference callbacks.
            publication.Restore(saved.Publication, saved.Publication.Current is null ? null : _standaloneWorldProjectionAttempt);
            _standaloneWorldProjectionConstructionFailure = saved.ConstructionFailure;
            return publication;
        }
        try
        {
            var inputs = FalloutWorldProjectionInputs.Read(source, stack, records.IniSettings);
            var consumers = new FalloutWorldProjectionConsumers(_standaloneWorldProjectionAttempt!, inputs, readCurrentTarget);
            _standaloneWorldProjectionAttempt!.ConstructOrdinary(records.IniSettings, consumers);
            publication.PublishReturned(_standaloneWorldProjectionAttempt);
            return publication;
        }
        catch (Exception failure)
        {
            _standaloneWorldProjectionConstructionFailure = failure.Message; throw;
        }
    }

    internal FalloutPlayerReferenceOrientationBinding ReadStandalonePlayerReferenceOrientationBinding()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var actor = ReadCombatActorIdentity(_enginePlayer);
        if (!actor.EnginePlayer || actor.Reference != _enginePlayer)
            throw new InvalidDataException("Player orientation lost its actual source Player reference/base owner.");
        return FalloutPlayerReferenceOrientationBinding.Read(records, actor.Reference, actor.Base, PrepareStandaloneWorldOrientationSource());
    }

    internal FalloutReferenceWorldProjectionSnapshot CaptureStandaloneWorldProjection()
    {
        var publication = CampaignStandaloneWorldProjection;
        var current = publication.Capture();
        var saved = new FalloutReferenceWorldProjectionSnapshot(FalloutReferenceWorldProjectionSnapshot.CurrentSchema, current,
            current.Current is null ? _standaloneWorldProjectionAttempt?.Capture() : null, _standaloneWorldProjectionConstructionFailure);
        saved.Validate(); return saved;
    }

    internal void RetireStandaloneWorldProjection()
    {
        if (_standaloneWorldProjectionRetired) return;
        if (_standaloneWorldProjection is { } publication)
        {
            publication.RequireCurrent(); publication.RetireCurrent();
            _standaloneWorldProjectionAttempt?.Retire();
        }
        _standaloneWorldProjectionRetired = true;
    }
}
