# HideAndShoot Stage 04 — static level/anchor audit

Source: Assets/HideAndShoot/Scenes/GameScene.unity

- PASS: mapped city 72×72m prototype root
- PASS: one central park, 2 named accessible-roof objects, 2 walkable ramp primitives
- PASS: nine uniquely named 3×3 player spawn points
- PASS: sixteen uniquely named ammo pickup candidate positions
- PASS: NavMeshSurface baked-data marker present
- PASS: player placement algorithm shuffles without replacement (SpawnPointRegistry.cs)
- PENDING: Unity compiled/attached SpawnPointRegistry and MatchAvatarBootstrap
- PENDING: live NavMesh connectivity for NPCs, walkability of rooftop slopes, real collision
- PENDING: 2–8 network avatar spawns, late join/round refresh and load-race tests

No Play/GUI tests were run for this stage.
