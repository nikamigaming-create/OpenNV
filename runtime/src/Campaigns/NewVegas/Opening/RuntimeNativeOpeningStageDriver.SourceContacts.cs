namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private Func<string?>? _sourceContactContinuation;

    internal void BindSourceContactContinuation(Func<string?> observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        if (_sourceContactContinuation is not null)
            throw new InvalidOperationException("Current source contact continuation observer was already bound.");
        _sourceContactContinuation = observe;
    }

    private string? SourceContactSaveBlocker => _sourceContactContinuation is { } observe
        ? observe() : "source-contact-native-owner-absent";

    private void RequireSourceContactContinuation()
    {
        if (SourceContactSaveBlocker is { } blocker)
            throw new NotSupportedException("Current native contact continuation cannot be captured: " + blocker);
    }
}
