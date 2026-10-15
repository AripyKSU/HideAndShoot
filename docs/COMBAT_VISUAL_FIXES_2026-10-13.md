# Combat visuals and HUD fixes — 2026-10-13

- Both pistol models use the Kyle right palm, upright wrist alignment, curled right fingers and a mesh-derived muzzle. The world animator holds the pistol with a masked right-arm layer while locomotion continues; the FPS animator disables that layer and holds its separate aim pose.
- The camera center ray supplies a single ordered collision result for the owner's target candidate and tracer endpoint. The beam converges from the actual muzzle to that endpoint. Close hits are no longer extended to 2m. ADS changes FOV without moving the camera through nearby geometry. Observer endpoints are resolved on the host before damage can disable target colliders.
- The imported ammunition pack is normalized to 42cm and centered above its spawn anchor. Material emission and renderers are enabled, and pooled respawns reset visual position and rotation without accumulating scale changes.
- The HUD uses centered geometric crosshair elements, an ammo meter with low/empty colors, pickup/empty feedback, contextual controls, and a Tab scoreboard. The ESC modal hides the HUD. The lobby remembers valid nicknames, supports Enter to join and displays a connection progress label.

## Verification

Unity 6000.3.18f1 compiled the runtime and editor scripts. Ten edit-mode physics/visual regression checks passed; see `Assets/HideAndShoot/QA/CombatVisualRegression.txt`. FPS, ADS, third-person, ammo and HUD images were rendered and inspected in the same QA directory.

Rerun with **Tools > HideAndShoot > Verify Combat Visuals and Aim** while outside Play mode. The installer menu saves normalized weapon/ammo prefabs without rebuilding gameplay scenes.

Online host/client input, historical hit validation under latency, and round-to-round play have not been exercised in a connected session. Existing host-only damage and ammunition authority remain in place.
