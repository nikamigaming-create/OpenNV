// OpenXR acceptance consumes shared runtime state, while coordinator owns its
// invocation. Keep that compile-time join here so both owners stay unchanged.
global using OpenNV.Runtime;
global using OpenNV.Runtime.Presentation.OpenXR;
