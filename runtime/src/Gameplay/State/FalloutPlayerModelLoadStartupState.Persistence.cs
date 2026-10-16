using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerModelLoadStartupState
{
    internal FalloutPlayerModelStartupSnapshot Capture()
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Entered Player startup-byte owner cannot be captured."); }
        var saved = new FalloutPlayerModelStartupSnapshot(Schema, Source, Stack, _recordLoad.SourceHash,
            Process, Factory, Constructor, _sequence, _value, Array.AsReadOnly(_publications.ToArray()),
            Array.AsReadOnly(_calls.ToArray()), _writerBoundary, _failure, _retired);
        Validate(saved, Source, Stack, _records); return saved;
    }

    internal static void Validate(FalloutPlayerModelStartupSnapshot saved, FalloutPlayerModelLoadPrerequisiteSource source,
        string stack, FalloutPluginStack records)
    {
        ArgumentNullException.ThrowIfNull(saved); ArgumentNullException.ThrowIfNull(records); source.Validate(); saved.Source.Validate();
        var receipt = records.LoadReceipt; records.RequireLiveLoadReceipt(receipt); var recordsSource = receipt.SourceHash;
        if (saved.Schema != Schema || saved.Source.Identity != source.Identity || saved.Stack != stack || saved.RecordsSource != recordsSource ||
            string.IsNullOrWhiteSpace(stack) || string.IsNullOrWhiteSpace(recordsSource) || saved.Process == Guid.Empty ||
            saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty || saved.Publications is not { Count: > 0 } ||
            saved.Calls is null || saved.Sequence < 1 || saved.WriterBoundary is { Length: 0 } || saved.Failure is { Length: 0 })
            throw new InvalidDataException("Startup-byte snapshot changed its selected source, actual reader or current process.");
        var processes = new HashSet<Guid>(); var factories = new HashSet<Guid>();
        var constructors = new HashSet<Guid>(); var readers = new HashSet<Guid>(); var calls = new HashSet<Guid>();
        var nextPublication = 0; var nextCall = 0; long sequence = 0;
        FalloutPlayerModelStartupPublication? current = null;
        byte value = 0; string? boundary = null, failure = null;
        while (nextPublication < saved.Publications.Count || nextCall < saved.Calls.Count)
        {
            var publication = nextPublication < saved.Publications.Count ? saved.Publications[nextPublication] : null;
            var call = nextCall < saved.Calls.Count ? saved.Calls[nextCall] : null;
            if (publication is not null && (call is null || publication.Published < call.Entered))
            {
                if (publication.Published != ++sequence || publication.Value != FalloutPlayerModelLoadPrerequisiteSource.LoaderInitializedCharacterGenerationByte ||
                    publication.Process == Guid.Empty || publication.Factory == Guid.Empty || publication.Constructor == Guid.Empty ||
                    publication.RecordsFactory == Guid.Empty || !processes.Add(publication.Process) || !factories.Add(publication.Factory) ||
                    !constructors.Add(publication.Constructor) || !readers.Add(publication.RecordsFactory) ||
                    (current is null ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                        publication.PreviousProcess != current.Process || publication.PreviousFactory != current.Factory))
                    throw new InvalidDataException("Startup-byte history disconnected or replayed its actual fresh PE loader owner.");
                current = publication; value = publication.Value; boundary = null; failure = null; ++nextPublication; continue;
            }
            var actual = call!; var invocation = actual.Invocation;
            if (current is null || invocation.Call == Guid.Empty || !calls.Add(invocation.Call) || invocation.Process != current.Process ||
                invocation.ThreadId < 1 || !Enum.IsDefined(invocation.Ingress) || actual.Factory != current.Factory || actual.Constructor != current.Constructor ||
                actual.Entered != ++sequence || actual.Before != value || actual.Returned != (actual.Failure is null) ||
                actual.Failure is { Length: 0 } || !Enum.IsDefined(actual.Step) || actual.EnclosingHelperReturned ||
                (actual.Step == FalloutPlayerModelStartupStep.Returned) != actual.Returned || actual.WriterBoundary is { Length: 0 })
                throw new InvalidDataException("Startup-byte operation replaced its actual call, constructor or failed source prefix.");
            if (actual.Stored is { } stored)
            {
                if (stored != ++sequence || invocation.Ingress == FalloutPlayerModelStartupIngress.UnownedWriter || actual.WriterBoundary is not null ||
                    invocation.Ingress == FalloutPlayerModelStartupIngress.CharacterGenerationStart &&
                    (invocation.Quest is null || actual.SelectedQuest != invocation.Quest || !ValidQuest(actual)) ||
                    invocation.Ingress == FalloutPlayerModelStartupIngress.LoadingMenuTeardown &&
                    (invocation.Quest is not null || actual.SelectedQuest is not null || actual.Setting is not null))
                    throw new InvalidDataException("Startup-byte store lost its selected Quest/current destructor or real store sequence.");
                value = invocation.Ingress == FalloutPlayerModelStartupIngress.CharacterGenerationStart
                    ? FalloutPlayerModelLoadPrerequisiteSource.EnteredCharacterGenerationByte
                    : FalloutPlayerModelLoadPrerequisiteSource.LoaderInitializedCharacterGenerationByte;
                if (invocation.Ingress == FalloutPlayerModelStartupIngress.LoadingMenuTeardown) boundary = null;
            }
            else if (actual.Returned && invocation.Ingress != FalloutPlayerModelStartupIngress.UnownedWriter)
                throw new InvalidDataException("Returned startup-byte setter omitted its mandatory actual store.");
            if (actual.WriterBoundary is { } writer)
            {
                if (invocation.Ingress != FalloutPlayerModelStartupIngress.UnownedWriter || invocation.Quest is not null ||
                    actual.Stored is not null || actual.SelectedQuest is not null || actual.Setting is not null)
                    throw new InvalidDataException("Startup-byte invalidation invented a source value or another writer arm.");
                boundary = writer;
            }
            else if (actual.Returned && invocation.Ingress == FalloutPlayerModelStartupIngress.UnownedWriter)
                throw new InvalidDataException("Returned unknown startup-byte writer lost its precise boundary.");
            if (actual.After != value || actual.Changed != ++sequence)
                throw new InvalidDataException("Startup-byte history differs from its original current stores.");
            failure ??= actual.Failure; ++nextCall;
        }
        if (current is null || current.Process != saved.Process || current.Factory != saved.Factory || current.Constructor != saved.Constructor ||
            saved.Value != value || saved.Sequence != sequence || saved.WriterBoundary != boundary || saved.Failure != failure)
            throw new InvalidDataException("Startup-byte snapshot forged its current value, boundary or connected fresh process.");

        bool ValidQuest(FalloutPlayerModelStartupCall call)
        {
            if (call.SelectedQuest is not { } quest || !uint.TryParse(call.Setting,
                System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var runtimeId) || runtimeId == 0)
                return false;
            var selected = records.GetEffective(quest);
            return selected.Signature == "QUST" && records.RuntimeFormId(quest) == runtimeId;
        }
    }
}
