using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutQuestScripts
{
    internal NativeNvseLocalAuthority NativeLocalAuthority(FalloutFormKey quest) =>
        FalloutNativePluginLocals.BindQuest(_records, _quests, ScriptValues, quest);
}
