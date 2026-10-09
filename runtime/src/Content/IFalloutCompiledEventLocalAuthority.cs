namespace OpenNV.Runtime.Content;

// Independent original producers (magic effects, immediate source callbacks)
// can own a genuine event list without attaching that Script to a placed actor.
// Implementations must validate the actual entered instance/event and retain its
// source cells; implementing a declaration or returning a boolean cannot admit
// execution. The common VM still creates/validates every shared lease itself.
internal interface IFalloutCompiledEventLocalAuthority
{
    FalloutScriptEffectLocals Locals { get; }
    void Require(FalloutPluginStack records, FalloutFormKey target, FalloutCompiledScriptProgram program,
        FalloutCompiledEvent block, FalloutCompiledExecutionCursor cursor, double seconds, FalloutFormKey? action);
    FalloutScriptValue Read(FalloutCompiledVariable variable);
    void Write(FalloutCompiledVariable variable, FalloutScriptValue value);
}
