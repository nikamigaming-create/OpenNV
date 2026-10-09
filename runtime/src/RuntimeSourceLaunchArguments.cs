using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

// The source startup consumes this product process's actual WinMain vector.
// Presentation switches are retained as real arguments; their presence cannot
// manufacture an empty source vector or an omitted configuration producer.
internal static class RuntimeSourceLaunchArguments
{
    internal static FalloutSourceLaunchArguments Read(RuntimeLiveContentSource source, string configuration)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Selected WinMain arguments require their actual Windows producer.");
        if (!FalloutAdvancementRuntimeReceipt.Digest(configuration)) throw new InvalidDataException("Source launch omitted its selected runtime configuration.");
        var command = GetCommandLineW();
        if (command == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual product GetCommandLineW returned no vector.");
        var vector = CommandLineToArgvW(command, out var count);
        if (vector == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual product CommandLineToArgvW failed.");
        Exception? originalFailure = null;
        try
        {
            if (count is < 1 or > 65536) throw new InvalidDataException("Actual product argument vector has an invalid extent.");
            var arguments = new string?[count];
            for (var index = 0; index < count; index++)
            {
                var pointer = Marshal.ReadIntPtr(vector, checked(index * IntPtr.Size));
                if (pointer == 0) throw new InvalidDataException("Actual product argument vector has a null owned entry.");
                arguments[index] = Marshal.PtrToStringUni(pointer) ?? throw new InvalidDataException("Actual product argument entry could not be read.");
            }
            var identity = FalloutAdvancementRuntimeReceipt.Hash(configuration + "\0" + source.StackId + "\0" + source.SaveCompatibilityId);
            var result = new FalloutSourceLaunchArguments("actual-product-WinMain/" + Environment.ProcessId,
                identity, Array.AsReadOnly(arguments));
            result.Validate(); return result;
        }
        catch (Exception failure) { originalFailure = failure; throw; }
        finally
        {
            if (LocalFree(vector) != 0)
            {
                var cleanup = new Win32Exception(Marshal.GetLastWin32Error(), "Actual product argument allocation did not retire.");
                if (originalFailure is not null) throw new AggregateException("Product argument read and retirement failed.", originalFailure, cleanup);
                throw cleanup;
            }
        }
    }
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint GetCommandLineW();
    [DllImport("shell32.dll", ExactSpelling = true, SetLastError = true)] private static extern nint CommandLineToArgvW(nint command, out int count);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern nint LocalFree(nint memory);
}
