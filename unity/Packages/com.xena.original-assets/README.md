# Xena original asset import

Local Unity 6.3 / URP package for `.xenamodel` files generated from the user's disc.
There are no original game assets in the package source.

`XenaModelImporter` creates a prefab, mesh/material/texture subassets and sampled
legacy AnimationClips. `Tools > Xena > Create Asset Preview Scene` stages an asset
preview. The component supports play, pause, frames, model rotation and 4x textures.

`XenaImportVerification.RunBatch` performs actual import/sampling checks when run
inside Unity. Until that command has passed, the offline .NET check only verifies
the shared schema and converted data; it does not compile or run Unity APIs.

Full workflow, documented assumptions and current verification results:
`decomp/docs/unity-import.md` in the source workspace.
