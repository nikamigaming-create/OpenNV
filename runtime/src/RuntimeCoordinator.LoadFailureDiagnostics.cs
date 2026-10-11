using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private sealed record NativeLoadFailureDiagnostic(long CapturedUtcTicks, ulong NativeFrame,
        string Error, string? Campaign, string? Source, JsonElement MainCaller,
        JsonElement MainPlayer, JsonElement PendingCell, JsonElement ImageModifiers,
        JsonElement MainImages, JsonElement NativeImages,
        IReadOnlyList<string> CaptureFailures);

    private NativeLoadFailureDiagnostic? _nativeLoadFailureDiagnostic;
    private static NativeLoadFailureDiagnostic? _nextSessionLoadFailureDiagnostic;
    private static readonly JsonSerializerOptions LoadFailureJson = new(JsonSerializerDefaults.Web);

    private void RetainNativeLoadFailureDiagnostic(Exception failure)
    {
        var captureFailures = new List<string>();
        JsonElement Capture(string lane, Func<object?> read)
        {
            try { return JsonSerializer.SerializeToElement(read(), LoadFailureJson); }
            catch (Exception captureFailure)
            {
                captureFailures.Add(lane + ": " + captureFailure);
                return JsonSerializer.SerializeToElement<object?>(null, LoadFailureJson);
            }
        }
        // Detached diagnostics survive real owner retirement. They never enter
        // a save, construct a replacement owner or supply gameplay authority.
        var caller = Capture("MainCaller", () => _nativeOpeningStageDriver?.SourceMainScriptCallerState ??
            _nativeReferences?.CampaignMainScriptCallerState);
        var player = Capture("MainPlayer", () => _nativeReferences?.CampaignMainPlayerCellState);
        var pending = Capture("PendingCell", () => _nativeBootstrap?.State);
        var modifiers = Capture("ImageModifiers", () => _nativeImageSpaceState.Active.Select(active => new
        {
            source = active.Source.Form.ToString(),
            active.Source.SourceSha256,
            active.ElapsedSeconds,
            active.Source.Duration
        }).ToArray());
        var images = Capture("MainImages", () => _nativeImageSpaceState.HasSourceMainImages
            ? _nativeImageSpaceState.CaptureSourceMainImages() : null);
        var native = Capture("NativeImages", () => new
        {
            fields = _nativeMainImagePresenter?.SourceFields,
            failure = _nativeMainImageApplication?.Effect.SourceDrawFailure,
            operational = _nativeMainImageApplication?.Effect.Operational
        });
        _nativeLoadFailureDiagnostic = new(DateTime.UtcNow.Ticks, Engine.GetProcessFrames(), failure.ToString(),
            _options.GetValueOrDefault("campaign"), RuntimeLiveContentSource.Current?.StackId,
            caller, player, pending, modifiers, images, native, captureFailures);
    }
}
