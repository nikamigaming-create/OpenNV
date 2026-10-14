namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutGameTime
{
    internal void BindActorFrameCalendar(OpenNV.Runtime.Content.FalloutPlayerLateActorTimeSource source,
        string stack, Guid process, Action requireLifetime) =>
        _globals.BindPlayerFrameCalendar(source, stack, process, _forms.Day, requireLifetime);

    internal FalloutPlayerLateCalendarDayRead ReadActorCalendarDay(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual) =>
        _globals.ReadPlayerFrameCalendarDay(_forms.Day, invocation, requireActual);

    internal static int ActorFrameSignedDay(uint bits)
    {
        // The selected getter performs CVTTSS2SI; its caller then sign-extends
        // AL. Keep the two conversions independent of date validation.
        var value = BitConverter.UInt32BitsToSingle(bits);
        var integer = !float.IsFinite(value) || value < int.MinValue || (double)value > int.MaxValue
            ? int.MinValue : (int)value;
        return unchecked((sbyte)integer);
    }
}
