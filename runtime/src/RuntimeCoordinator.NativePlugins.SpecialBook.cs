using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutSpecialBookPageSounds? CreateNativeSpecialBookPageSounds(RuntimeLiveContentSource source,
        FalloutPluginStack records, FalloutQuestScripts scripts, FalloutReferenceWorld world)
    {
        if (source.Game != RuntimeLiveContentSource.FalloutNewVegasGame) return null;
        var selected = FalloutSpecialBookPageSoundsSource.Read(source.FalloutExecutablePath);
        var productThread = System.Environment.CurrentManagedThreadId; FalloutSpecialBookPageSounds? field = null;
        field = new(selected, () =>
        {
            if (System.Environment.CurrentManagedThreadId != productThread || !ReferenceEquals(RuntimeLiveContentSource.Current, source) ||
                !ReferenceEquals(_nativePluginStack, records) || !ReferenceEquals(_nativeQuestScripts?.Scripts, scripts) ||
                !ReferenceEquals(scripts.References, world))
                throw new InvalidOperationException("Book sound inputs left their actual product/source/records/World lifetime.");
        }, () =>
        {
            if (field is { ActiveConsumers: not 0 }) throw new InvalidOperationException("Book sound inputs retain actual menu/audio consumers.");
        }, records, FalloutSpecialBookFocusSoundsSource.Read(source.FalloutExecutablePath));
        return field;
    }
}
