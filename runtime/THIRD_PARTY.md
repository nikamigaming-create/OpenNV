# Runtime dependencies

OpenNV experimental Windows packages contain the official Godot Engine 4.7.2
Mono export runtime and the .NET 8 runtime needed by the C# game assembly.
Godot and .NET are distributed under the MIT license. Their license texts are
included in the package's `licenses` directory.

Godot's complete dependency notices are in `licenses/Godot-COPYRIGHT.txt`
and at <https://godotengine.org/license/>. The .NET runtime's dependency
notices are in `licenses/Dotnet-THIRD-PARTY-NOTICES.txt`.

Owned game data is read in place and is never included. Packages contain only
the first-party code/resources and the listed runtime dependencies; no separate
content helper is needed. Other notices supplied by the official export runtime
must be retained alongside these license texts.
