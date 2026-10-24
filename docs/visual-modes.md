# Original / Remaster presentation

Press F10 in the movement prototype, including during combat, flight or pause.
The HUD shows the selected mode. Save settings in the pause menu to retain it;
restoring defaults selects Remaster. F10 is currently a fixed shortcut, separate
from the rebindable gameplay actions, and respects the test's input-device scope.

Current Original: original texture/material bindings and no added chakram halo.
Current Remaster: existing upscale bindings and expanding rotational afterimages.
Both currently share original geometry and original lighting. Original does not
claim PS1 rasterizer, framebuffer, affine texture or dithering parity; Remaster
does not yet contain new high-detail models. Both retain collision-impact sparks.

`PrototypeGraphics` owns presentation selection and notifies `IXenaVisualModeTarget`
providers and its `Changed` event. All live `XenaAssetPreview` components switch
their existing original/upscale material sets. Position, motor, animation clock,
combat, AI, colliders, level triggers and saves remain one shared simulation.
The manager is installed by the prototype motor; imported asset preview scenes
without the motor retain their existing standalone controls.

`XenaVisualVariant` accepts original/remaster Renderer arrays for a future matched
model pair and toggles only renderers. Original remains visible until an improved
set is supplied. Bind future models to a shared pose provider (same actor/pose
clock, compatible rig or explicit animation mapping); do not duplicate controllers,
physics or gameplay roots and do not deactivate actor roots when switching.
Future lighting/postprocessing/effect providers implement the same interface.
Newly spawned variants apply the current mode on enable. Skinned model retargeting,
HD assets, a PS1 shader mode and split-screen comparison remain future work.

The chakram halo uses local-plane translucent arc meshes, not a world-space trail.
Four short afterimages at different ages expand from about0.145m to0.20m and fade
over0.16sec, with angular streaks following the disc rotation. The original opaque
disc stays sharp. Halo geometry freezes onpause and clears oncatch/reset.

Windows checks exercise real F10 input during flight, real material replacement,
halo visibility, unchanged projectile/animation state on mode selection, unchanged
mid-spin combat state and mode persistence/default restoration across processes.
