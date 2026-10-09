using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class RuntimeNativeQuestScripts
{
    internal void BindDirectInput(FalloutDirectInputState input)
    {
        if (!ReferenceEquals(Scripts.Controls, input.Controls))
            throw new InvalidDataException("Quest input events and native device state differ from the actual source controls.");
        _events.BindDirectInput(input);
    }
}
