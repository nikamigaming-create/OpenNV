# Source texture alpha

BC1 stores one-bit transparency in its texel selectors. In three-colour mode
(first endpoint less than or equal to the second), selector three is
transparent. In four-colour mode, that selector remains an opaque interpolated
colour. A DDS header need not separately flag these transparent texels.

The direct DDS loaders previously passed BC1 straight to Godot's DXT1 upload.
On the selected Forward+ build, that path samples alpha as one, producing black
fill between the gurney wheel spokes despite the correct NIF alpha test.
The earlier no-lighting falloff correction concerns a separate gurney surface.

The shared texture upload owner now examines all authored mip levels. BC1
images containing transparent texels expand to RGBA8 in memory before upload;
opaque BC1 stays compressed. Padding selectors outside a level's dimensions do
not trigger expansion. Truncated, trailing and excessive mip payloads fail.
The source files, colours, alpha-test threshold and geometry remain unchanged.
No replacement mip levels or persistent texture inputs are produced.

The owner serves native NIF textures, NPC texture overrides, FaceGen textures,
owned UI/media and the existing material loader. Cubemaps retain a common
format across all faces when a face requires alpha preservation. Texture
telemetry records the source format, upload format and alpha handling.

## Verification

Synthetic contracts cover both endpoint modes, equal endpoints, selectors,
partial blocks, lower-mip transparency and malformed extents. The native image
audit verifies exact authored RGBA/mip bytes, unchanged opaque compression and
consistent cubemap faces. These checks run in the required repository gate.

The optional Forward+ GPU audit compares sampled alpha at every texel of every
mip, including a negative control using the original upload:

```powershell
& $Godot --path runtime --rendering-method forward_plus res://tools/NativeNifInstanceAudit/NativeNifInstanceAudit.tscn -- --dds-gpu $DataRoot $TexturePath
```

The selected owned gurney DDS has ten mip levels and 349,525 texels. The original
upload differs on 63,464 alpha samples; the corrected upload has zero alpha
mismatches. The four-level synthetic fixture also passes and detects the old
failure. These sampler checks do not establish matched scene lighting,
shadows, camera state or final retail pixels. Ordinary room-79 accepts original
creation and reaches free movement at stage 55. Its close-up shows open spokes
on the large wheels and small caster. Live telemetry binds the alpha-bearing
texture to RGBA8 while its opaque BC1 mask and BC3 normal retain their original
formats and hashes. The current retail/OpenNV views are not aligned.
The selected owned rendering audit and full repository gate pass.
