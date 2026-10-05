using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// This is an actual stopped invocation,
// separate from the source's mutable locals and from the completed speech voice.
internal sealed record FalloutReferenceScriptStoppedFrame(string Error, string SourceSha256,
    int Block, int Statement, string Event, string? Filter, double ElapsedSeconds,
    FalloutFormKey? ActionReference, FalloutFinishedSpeechReceipt? Speech,
    FalloutDetectionEventRequest? PreparedDetection, string? OriginalError = null)
{
    internal void Validate(string? retainedError, string selectedSha256,
        IReadOnlyList<FalloutScriptEventProgram> sourceBlocks)
    {
        if (string.IsNullOrWhiteSpace(Error) || Error != retainedError || SourceSha256 != selectedSha256 ||
            SourceSha256.Length != 64 || !SourceSha256.All(Uri.IsHexDigit) ||
            Block < 0 || Block >= sourceBlocks.Count || Statement < 0 || Statement >= sourceBlocks[Block].Program.StatementCount ||
            !double.IsFinite(ElapsedSeconds) || ElapsedSeconds < 0 ||
            !Event.Equals(sourceBlocks[Block].Event, StringComparison.OrdinalIgnoreCase) ||
            Filter != sourceBlocks[Block].Filter)
            throw new InvalidDataException("Stopped reference script lost its source block, fault or original invocation.");
        if (OriginalError is not null && string.IsNullOrWhiteSpace(OriginalError))
            throw new InvalidDataException("Stopped source invocation has an absent historical fault.");
        if (PreparedDetection is not null && !sourceBlocks[Block].Program.CommandSites("CreateDetectionEvent").Any(site => site.Statement == Statement))
            throw new InvalidDataException("Prepared detection request is bound to another source instruction.");
        if (!(Event.Equals("GameMode", StringComparison.OrdinalIgnoreCase) || Event.Equals("SayToDone", StringComparison.OrdinalIgnoreCase)) ||
            (Event.Equals("SayToDone", StringComparison.OrdinalIgnoreCase) ? Speech is null : Speech is not null))
            throw new InvalidDataException("Stopped reference event has conflicting voice completion scope.");
    }

    internal FalloutReferenceScriptStoppedFrame Copy() => this with
    {
        Speech = Speech?.Copy(),
        PreparedDetection = PreparedDetection is null ? null : PreparedDetection with { Position = PreparedDetection.Position.ToArray() }
    };

    internal FalloutGameModeProgram DetectionContinuation(IReadOnlyList<FalloutScriptEventProgram> sourceBlocks,
        Func<string, bool> fixedOperand)
    {
        if (PreparedDetection is null)
            throw new NotSupportedException("Stopped detection command has no retained original request.");
        var marker = Event + ": ";
        if (!Error.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stopped reference error does not belong to its original event.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(Error[marker.Length..],
            @"^Reached (?:native script|object-script) command (?:\S+\.)?CreateDetectionEvent \([23] arguments\) has no owner\.$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new NotSupportedException("Stopped source operation is not the newly owned detection command.");
        // Admission is specific to the newly owned command. Its executing owner
        // validates the original argument/request relationship before effects,
        // then advances the following matching source blocks in authored order.
        return sourceBlocks[Block].Program.MissingCommandContinuation(Error[marker.Length..], fixedOperand, Statement)
            ?? throw new NotSupportedException("Stopped detection command requires an unretained loop or mutable execution frame.");
    }
}
