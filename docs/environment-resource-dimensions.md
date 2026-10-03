# Environment resource dimensions

The owned NIF environment slot and DDS resource dimensionality are independent.
An original TTW glasses model declares an environment shader input but resolves
it to a 512x64 BC1 2D DDS with ten authored mips and no cube caps. Rejecting it as
an incomplete cubemap prevents the entire real NPC from loading and removes its
native package owner.

The loader validates the DDS header and preserves 2D resources through the
ordinary DDS upload owner. Complete six-face cubes retain their source face
mapping, alpha and authored mips; incomplete cubes and unsupported dimensions
remain errors. Shader composition binds a separate 2D sampler and ignores the
excess reflection coordinate for that resource, retaining source mask, scale and
light-fade composition. It does not generate cube faces or persist converted
assets. This follows the dimensional lookup rule in
[Microsoft's D3D9 texld contract](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/texld---ps-2-0).
Owned shader observations remain private and are reduced to semantic contracts.

Native synthetic checks cover 2D identity/mips, complete cubes and invalid faces.
The winning diner booths co-declare the environment-fade aliases at bits13/15.
The material selector retains those flags on the actual environment path rather
than interpreting the co-declared alias as an unsupported tree variant. Ordinary
lighting also retains bit14 without selecting a landscape/LOD path. Synthetic
flag negatives and the owned booth build pass; six original booth references
admit in the native birthday room. Other tree/landscape semantics remain unbound.
Full source reflection-vector timing, filtering, shader variant selection and
matched retail final pixels remain unverified. Resource admission alone is not
actor or campaign parity.
