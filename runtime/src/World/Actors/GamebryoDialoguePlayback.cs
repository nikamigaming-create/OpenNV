using System.Globalization;
using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed record SourceDialogueAsset(
    string LogicalPath,
    string SourcePath,
    string Sha256);

internal sealed record SourceDialogueLine(
    string InfoFormId,
    int ResponseIndex,
    string SpeakerIdentity,
    string Text,
    SourceDialogueAsset Voice,
    SourceDialogueAsset Lip);

internal sealed record SourceDialogueInfoCandidate<TInfo, TCondition>(
    string InfoFormId,
    int SourceOrder,
    bool SayOnce,
    IReadOnlyList<TCondition> Conditions,
    TInfo Value);

internal sealed record SourceDialogueInfoSelection<TInfo>(TInfo Value, int NextCursor);

internal sealed class GamebryoDialoguePlayback
{
    private static readonly int Sha256HexLength =
        Convert.ToHexString(SHA256.HashData([])).Length;
    private readonly AudioStreamPlayer _voice;
    private readonly FaceGenLipConfiguration _lipConfiguration;
    private FaceGenLipAnimation? _lip;
    private FaceGenMorphController? _face;
    private Action? _completed;
    private int _generation;

    internal GamebryoDialoguePlayback(
        AudioStreamPlayer voice,
        FaceGenLipConfiguration lipConfiguration)
    {
        _voice = voice;
        _lipConfiguration = lipConfiguration;
        _voice.Finished += Complete;
    }

    internal AudioStreamPlayer Voice => _voice;
    internal SourceDialogueLine? ActiveLine { get; private set; }

    internal double PositionSeconds => _voice.Playing
        ? _voice.GetPlaybackPosition()
        : 0.0;

    internal void Start(
        SourceDialogueLine line,
        FaceGenMorphController face,
        Action completed)
    {
        ValidateLine(line);
        Stop();
        var stream = AudioStreamOggVorbis.LoadFromFile(line.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Source dialogue voice could not be decoded: {line.Voice.LogicalPath}");
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException(
                $"Source dialogue voice has no duration: {line.Voice.LogicalPath}");
        _lip = FaceGenLipAnimation.Load(line.Lip.SourcePath, _lipConfiguration);
        _face = face;
        ActiveLine = line;
        var generation = ++_generation;
        _completed = () =>
        {
            if (generation != _generation)
                return;
            Stop();
            completed();
        };
        _voice.Stream = stream;
        _voice.SetMeta("opennv_info_form_id", line.InfoFormId);
        _voice.SetMeta("opennv_response_index", line.ResponseIndex);
        _voice.SetMeta("opennv_speaker_identity", line.SpeakerIdentity);
        _voice.Play();
    }

    internal void Update()
    {
        if (_completed is null || _lip is null || _face is null || !_voice.Playing)
            return;
        var line = ActiveLine ?? throw new InvalidOperationException(
            "Source dialogue playback lost its active INFO identity.");
        if (_voice.GetMeta("opennv_info_form_id").AsString() != line.InfoFormId ||
            _voice.GetMeta("opennv_response_index").AsInt32() != line.ResponseIndex ||
            _voice.GetMeta("opennv_speaker_identity").AsString() != line.SpeakerIdentity)
            throw new InvalidOperationException(
                "Source dialogue voice and LIP identities diverged.");
        _face.Apply(_lip, _voice.GetPlaybackPosition());
    }

    internal void Complete()
    {
        var completed = _completed;
        _completed = null;
        completed?.Invoke();
    }

    internal void Stop()
    {
        _completed = null;
        _face?.Clear();
        _lip = null;
        _face = null;
        ActiveLine = null;
        _generation++;
        if (_voice.Playing)
            _voice.Stop();
    }

    internal static void ValidateOrderedLines(IReadOnlyList<SourceDialogueLine> lines)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("Source dialogue has no response lines.");
        var priorInfo = lines[0].InfoFormId;
        var priorIndex = 0;
        foreach (var line in lines)
        {
            ValidateLine(line);
            if (line.InfoFormId.Equals(priorInfo, StringComparison.OrdinalIgnoreCase))
            {
                if (line.ResponseIndex <= priorIndex)
                    throw new InvalidOperationException(
                        "Source dialogue response order is not strictly increasing.");
            }
            else
            {
                priorInfo = line.InfoFormId;
                priorIndex = 0;
            }
            priorIndex = line.ResponseIndex;
        }
    }

    internal static SourceDialogueInfoSelection<TInfo>? SelectFirstInfo<TInfo, TCondition>(
        IReadOnlyList<SourceDialogueInfoCandidate<TInfo, TCondition>> orderedInfos,
        int cursor,
        IReadOnlySet<string> saidOnce,
        Func<TCondition, bool> evaluateCondition)
    {
        if (cursor < 0 || cursor > orderedInfos.Count ||
            orderedInfos.Count == 0 ||
            orderedInfos.Any(value => string.IsNullOrWhiteSpace(value.InfoFormId)) ||
            orderedInfos.Select(value => value.InfoFormId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != orderedInfos.Count ||
            orderedInfos.Zip(orderedInfos.Skip(1), (first, second) =>
                first.SourceOrder < second.SourceOrder).Any(ordered => !ordered))
            throw new InvalidOperationException(
                "Source dialogue INFO ordering is invalid.");
        while (cursor < orderedInfos.Count)
        {
            var candidate = orderedInfos[cursor++];
            if (candidate.SayOnce && saidOnce.Contains(candidate.InfoFormId))
                continue;
            if (!candidate.Conditions.All(evaluateCondition))
                continue;
            return new SourceDialogueInfoSelection<TInfo>(candidate.Value, cursor);
        }
        return null;
    }

    internal static (string QuestEditorId, int Stage) RequireStageResult(
        IReadOnlyList<string> orderedCommands)
    {
        if (orderedCommands.Count != 1)
            throw new InvalidOperationException(
                "Source dialogue result contains unsupported command semantics.");
        var tokens = orderedCommands[0].Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 3 ||
            !tokens[0].Equals("setstage", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(tokens[1]) ||
            !int.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stage) ||
            stage < 0)
            throw new InvalidOperationException(
                "Source dialogue result is not a supported stage handoff.");
        return (tokens[1], stage);
    }

    internal static (string QuestFormId, int Stage) RequireStageResult(
        string commandKind,
        string? questFormId,
        int? stage)
    {
        if (!commandKind.Equals("setStage", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(questFormId) ||
            stage is not { } target || target < 0)
            throw new InvalidOperationException(
                "Source dialogue result is not a supported stage handoff.");
        return (questFormId, target);
    }

    private static void ValidateLine(SourceDialogueLine line)
    {
        if (string.IsNullOrWhiteSpace(line.InfoFormId) ||
            line.ResponseIndex <= 0 ||
            string.IsNullOrWhiteSpace(line.SpeakerIdentity) ||
            string.IsNullOrWhiteSpace(line.Text))
            throw new InvalidOperationException("Source dialogue response is incomplete.");
        ValidateAsset(line.Voice, "voice");
        ValidateAsset(line.Lip, "LIP");
    }

    private static void ValidateAsset(SourceDialogueAsset asset, string kind)
    {
        if (string.IsNullOrWhiteSpace(asset.LogicalPath) ||
            string.IsNullOrWhiteSpace(asset.SourcePath) ||
            asset.Sha256.Length != Sha256HexLength ||
            asset.Sha256.Any(character => !Uri.IsHexDigit(character)) ||
            !File.Exists(asset.SourcePath))
            throw new InvalidOperationException(
                $"Source dialogue {kind} identity is incomplete.");
    }
}
