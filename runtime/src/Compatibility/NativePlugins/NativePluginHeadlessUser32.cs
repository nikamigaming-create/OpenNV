using System.Runtime.InteropServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativePluginHeadlessUser32 : INativePluginProductUser32
{
    private readonly int _managedThread = Environment.CurrentManagedThreadId;
    private readonly FalloutDirectInputState _input;
    private readonly NativePluginUser32Keyboard _keyboard;
    private int _readers;
    private bool _retired;
    public string SourceIdentity => _input.Source;
    public uint Window => 0;
    public uint Thread { get; }
    public uint Process { get; }

    internal NativePluginHeadlessUser32(FalloutDirectInputState input)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original USER32 APIs require real Windows.");
        input.RequireCurrent();
        if (input.Provider != FalloutInputProvider.HeadlessCommands || input.Window != 0)
            throw new InvalidDataException("Headless USER32 capability requires the actual windowless command input owner.");
        _input = input; Thread = GetCurrentThreadId(); Process = checked((uint)Environment.ProcessId);
        if (Thread == 0) throw new InvalidDataException("Headless USER32 capability omitted its actual native product thread.");
        _keyboard = new(input, Thread);
    }
    public void Require()
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this); _input.RequireCurrent();
        if (_input.Provider != FalloutInputProvider.HeadlessCommands || _input.Window != 0)
            throw new InvalidDataException("Headless USER32 capability changed its actual input provider.");
    }
    public IDisposable Retain()
    {
        Require(); var inputLease = _input.RetainNativeReader(); _readers++;
        return new ReaderLease(this, inputLease);
    }
    private sealed class ReaderLease(NativePluginHeadlessUser32 owner, IDisposable input) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (_released) return;
            owner.RequireThread(); if (owner._readers <= 0) throw new InvalidOperationException("Headless USER32 readers are unbalanced.");
            input.Dispose(); owner._readers--; _released = true;
        }
    }
    public NativePluginWindowResult Execute(NativePluginWindowOperation operation, uint target, uint incoming,
        uint childThread, ReadOnlySpan<byte> payload, Func<uint, (int Result, uint LastError)>? enumerate = null)
    {
        Require();
        if (!NativePluginUser32Keyboard.Handles(operation))
            throw new NotSupportedException("Reached USER32 " + operation + " requires its actual product-window/input producer; headless play owns no HWND.");
        if (target != 0 || enumerate is not null) throw new InvalidDataException("Headless keyboard API fabricated a window or enumeration callback.");
        return _keyboard.Execute(operation, incoming, childThread, payload);
    }
    public void RequireColdCapture() { Require(); _keyboard.RequireColdCapture(); }
    public void RetireBeforeChildExit(uint childThread) { Require(); }
    public void RetireAfterChildExit()
    {
        RequireThread(); if (_retired) return;
        if (_readers != 0) throw new InvalidOperationException("Headless USER32 readers remain after actual child closure.");
        _keyboard.Retire(); _retired = true;
    }
    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _managedThread || GetCurrentThreadId() != Thread)
            throw new InvalidOperationException("Headless USER32 capability moved away from its actual product thread.");
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
