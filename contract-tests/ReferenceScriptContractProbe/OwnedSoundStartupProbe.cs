using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class OwnedSoundStartupProbe
{
    internal static int Run(string root, string game, string[] options)
    {
        string? mod = null, modRoot = null;
        var additional = new List<string>();
        for (var index = 0; index < options.Length; index += 2)
        {
            if (index + 1 >= options.Length) throw new ArgumentException("Sound startup option has no value.");
            switch (options[index])
            {
                case "--mod-id" when mod is null: mod = options[index + 1]; break;
                case "--mod-root" when modRoot is null: modRoot = options[index + 1]; break;
                case "--mod-additional-root": additional.Add(options[index + 1]); break;
                default: throw new ArgumentException("Unknown or duplicate sound startup option: " + options[index]);
            }
        }
        if ((mod is null) != (modRoot is null) || mod is null && additional.Count != 0)
            throw new ArgumentException("Additional sound source folders require the actual selected mod identity and root.");
        RuntimeLiveContentSource Open() => mod is null ? RuntimeLiveContentSource.Open(root, game) :
            new FalloutModSelection(mod, modRoot!, additional).Resolve(root).OpenSource();
        FalloutMenuSoundSelectionSnapshot Capture(FalloutMenuSoundSelectionSnapshot? restore, out string stack, out int layers)
        {
            using var source = Open();
            if (source.Game != game) throw new InvalidDataException("Sound startup source differs from its selected edition.");
            using var records = FalloutPluginStack.Load(source.PluginSources);
            using var runtime = FalloutAdvancementRuntimeSource.Open(records);
            var selection = source.OpenMenuSoundSelection(records, runtime, restore);
            var saved = selection.Capture();
            var files = saved.FileManager ?? throw new InvalidDataException("Actual sound startup did not publish its file-manager owner.");
            if (files.Failure is not null || files.Invalidation?.UnownedReason is not null || files.Registry?.Failure is not null ||
                files.Registry?.Registrations.Any(row => row.Phase == FalloutArchiveRegistrationPhase.Failed) == true ||
                files.Modes.UseArchives != 0 && files.Registry is null || saved.Attempts.Count != 0)
                throw new InvalidDataException("Actual source sound registration failed or replayed a menu caller.");
            stack = source.StackId; layers = source.ContentRoots.Count;
            return saved;
        }
        var saved = Capture(null, out var stack, out var layers);
        var cold = Capture(saved, out var coldStack, out var coldLayers);
        if (stack != coldStack || layers != coldLayers || JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(cold))
            throw new InvalidDataException("Cold sound startup changed selected inputs or replayed a source caller.");
        Console.WriteLine("OPENNV_OWNED_SOUND_STARTUP_REPORT " + JsonSerializer.Serialize(new
        {
            game, mod, stack, layers, files = saved.FileManager!.Schema,
            registry = saved.FileManager.Registry?.Schema,
            registrations = saved.FileManager.Registry?.Registrations.Count ?? 0,
            invalidationBytes = saved.FileManager.Invalidation?.Bytes,
            sourceReadOnly = true, coldNoCallerReplay = true,
            layeredLooseEnumeration = "UNEXECUTED", nativeAudio = "UNEXECUTED", gameplay = "UNEXECUTED", parity = "UNVERIFIED"
        }));
        Console.WriteLine("OPENNV_OWNED_SOUND_STARTUP_PASS selectedNamespace=true sourceRegistry=true coldNoCallerReplay=true renderer=NONE gameplay=UNEXECUTED");
        return 0;
    }
}
