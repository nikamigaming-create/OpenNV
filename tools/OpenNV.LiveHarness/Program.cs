using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace OpenNV.LiveHarness;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--export", var exportConfiguration])
            {
                HarnessSbsExport.Run(exportConfiguration);
                return 0;
            }
            if (args.Length >= 3 && args[0] == "--send")
            {
                using var pipe = new NamedPipeClientStream(".", PipeName(args[1]), PipeDirection.InOut);
                pipe.Connect(5000);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                writer.WriteLine(args[2]);
                Console.WriteLine(reader.ReadLine());
                return 0;
            }
            var background = args is ["--background", _];
            if (background) args = [args[1]];
            if (args.Length != 1)
                throw new ArgumentException("Use OpenNV.LiveHarness [--background] <private-session.json> or --send <session> <JSON>.");
            var configuration = JsonSerializer.Deserialize<HarnessConfiguration>(File.ReadAllText(args[0]), Json)
                ?? throw new InvalidDataException("Missing harness configuration.");
            configuration.Validate();
            ApplicationConfiguration.Initialize();
            Application.Run(new HarnessWindow(configuration, background));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    internal static string PipeName(string session)
    {
        if (session.Length is < 1 or > 80 || session.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new ArgumentException("Invalid harness session name.");
        return "OpenNV.LiveHarness." + session;
    }
}

internal sealed record HarnessConfiguration(
    string Session,
    string Directory,
    int RetailProcessId,
    int OpenNvProcessId,
    string RetailCommandDirectory,
    string OpenNvCommandDirectory)
{
    internal void Validate()
    {
        _ = Program.PipeName(Session);
        if (RetailProcessId <= 0 || OpenNvProcessId <= 0 || RetailProcessId == OpenNvProcessId)
            throw new ArgumentException("Two distinct live game processes are required.");
        foreach (var path in new[] { Directory, RetailCommandDirectory, OpenNvCommandDirectory })
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException("Harness directories must be absolute private paths.");
        System.IO.Directory.CreateDirectory(Directory);
        System.IO.Directory.CreateDirectory(RetailCommandDirectory);
        System.IO.Directory.CreateDirectory(OpenNvCommandDirectory);
    }
}
