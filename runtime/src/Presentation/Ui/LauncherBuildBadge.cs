using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record LauncherBuildBadge(string Text, string Detail, string? Version = null, string? Commit = null, bool? Dirty = null)
{
    internal static LauncherBuildBadge ReadBesideExecutable() => Read(Path.Combine(
        Path.GetDirectoryName(OS.GetExecutablePath()) ?? AppContext.BaseDirectory, "release-manifest.json"));

    internal static LauncherBuildBadge Read(string path)
    {
        if (!System.IO.File.Exists(path)) return new("Development build", "No packaged build metadata was supplied.");
        try
        {
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.GetProperty("schema").GetString() != "opennv-release/v1")
                throw new InvalidDataException("Unknown build metadata schema.");
            var version = root.GetProperty("version").GetString();
            var commit = root.GetProperty("commit").GetString();
            if (string.IsNullOrWhiteSpace(version) || version.Length > 64 || version.Any(char.IsControl) ||
                commit is null || commit.Length != 40 || commit.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("Build metadata has an invalid version or source commit.");
            var dirty = root.GetProperty("dirty").GetBoolean();
            var experimental = root.GetProperty("experimental").GetBoolean();
            if (root.GetProperty("files").ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Build metadata has no file identity list.");
            var text = $"{version}  ·  {commit[..8]}" + (dirty ? "  ·  modified source" : string.Empty);
            return new(text, $"Source commit: {commit}\n" + (experimental ? "Experimental test package." : "Packaged build.") +
                (dirty ? " Source was modified when packaged." : string.Empty) + "\n" + path, version, commit, dirty);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {
            return new("Build metadata unavailable", "Could not read package identity: " + error.Message);
        }
    }
}
