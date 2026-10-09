using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutImagePhaseHourSource(FalloutFormKey Form, string GlobalSha256, string CalendarSha256);

internal sealed partial class FalloutGameTime
{
    internal FalloutImagePhaseHourSource ImagePhaseHourSource
    {
        get
        {
            var declaration = _globals.Sources.Single(source => source.Form == _forms.Hour);
            return new(declaration.Form, declaration.SourceSha256, _calendar.SourceSha256);
        }
    }
}
