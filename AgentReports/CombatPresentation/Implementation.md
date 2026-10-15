# Combat presentation implementation — 2026-10-15

## Implemented behavior

- Spectators see a thin off-white outline and a camera-facing nickname above each living player. Living players see an opponent's outline only during that opponent's NPC-hit penalty. Names remain spectator-only.
- Opaque walls occlude outlines. A full silhouette and linear character depth are drawn into an R32 texture and a separate depth attachment. One composite pass finds newly dilated pixels and checks both the source body depth and destination depth against scene depth. No expanded-mask intermediate or scene-color copy is needed. Thickness defaults to 2 pixels.
- Nicknames follow the animated head, use the character's render layer, have depth-tested materials, and hide when the head is blocked by a collider. Transparent materials and collider/render mismatches were not separately tested.
- The host grants a 10-second reveal only after approving the death of a living NPC. Repeated NPC hits refresh the deadline to server time + 10 seconds. Death and round end/reset clear it. The owner gets a separate bottom-center countdown warning.
- Player death approval records exactly one match kill and broadcasts one reliable sequenced kill-feed event. NPC kills, disconnects and duplicate deaths do not award kills. The feed shows at most five recent events for five seconds, with a final fade.
- The authored Tab scoreboard has an additional Kills column. The score and podium snapshot formats were advanced to v2 and carry kills.
- Ranking is wins descending, kills descending, then the previous rules: more recent scoring order for positive-win ties, earlier join order, client ID. Tab and podium use the same ranking function.
- Match kills persist across rounds, reset with the game scene/session, leave with disconnected players, and roll back for a voided redo round. Existing final-two outcome timing is preserved.

## Main source files

- Assets/HideAndShoot/Scripts/Combat/MatchCombatLedger.cs — match kills, duplicate suppression, redo rollback
- Assets/HideAndShoot/Scripts/Combat/HostPlayerLife.cs — server-owned reveal deadline and highlight registration
- Assets/HideAndShoot/Scripts/Networking/HostDeathResolver.cs — approved NPC/player-hit integration
- Assets/HideAndShoot/Scripts/Networking/RelayLobbySession.cs — v2 score serialization, ranking, reliable bounded kill feed
- Assets/HideAndShoot/Scripts/World/HostRoundSetupCoordinator.cs — round-end reveal cleanup
- Assets/HideAndShoot/Scripts/Player/PlayerHighlightTarget.cs — local visibility policy, render parts, spectator names
- Assets/HideAndShoot/Scripts/Player/PlayerOutlineRendererFeature.cs — URP Render Graph silhouette and composite passes
- Assets/HideAndShoot/Resources/Shaders/PlayerSilhouette.shader
- Assets/HideAndShoot/Resources/Shaders/PlayerOutline.shader
- Assets/HideAndShoot/Scripts/UI/GameHUDPresenter.cs — authored UI bindings, kills, feed and countdown
- Assets/HideAndShoot/Scenes/GameScene.unity — updated editable HUD
- Assets/Settings/PC_Renderer.asset and Mobile_Renderer.asset — outline feature installed through Unity CLI

Original versions of changed pre-existing files are in AgentBackups/20261015-CombatPresentation.

## Verification

All Unity interaction used the official CLI against C:/UnityProject/HideAndShoot. No copied project, standalone build, or external client was created.

### Edit mode

- C# compilation: 0 errors, 0 warnings.
- core_checks.cs: 14 assertions passed: duplicate victim, stale round, self kill, accumulation, rollback, disconnect cleanup, ranking and edit-mode execution.
- wire_checks.cs: 6 assertions passed: Unicode name/kill wire roundtrip, old score fields, flags, duplicate feed events and five-row bound.
- outline_checks.cs: actual GPU render — 772 white pixels exposed, 0 fully occluded, 386 partially occluded.
- skinned_checks.cs: actual PlayerAvatar skinned mesh and name — 1996 white pixels exposed, 0 fully occluded, 649 partially occluded; name creation and hiding checked. Captures include the nickname.
- hud_checks.cs: 1280x720 renders of eight 20-character names and five feed rows; scoreboard name overflow = 0.

### Exactly one play session

A local NGO host used actual PlayerAvatar prefabs and the NPC pool. Remote ownership IDs were synthetic, and approved-hit records were injected at HostDeathResolver's trusted input. This verifies the host-approved-hit-to-presentation path; it is not a Relay/multiple-device transport test or a manual aiming test.

- Frame advancement confirmed (863-frame delta at the hit check).
- Both local and synthetic remote NPC penalties started at 10 seconds; repeated NPC hit refreshed without stacking.
- Approved player death incremented kills and displayed the kill feed, warning and scoreboard data; repeated death did not increment again.
- Death cleared the local reveal and switched to spectator presentation.
- The target penalty expired naturally. Spectator outlines and names remained enabled independently of the penalty.
- An inserted opaque wall hid the spectator nickname.
- Round reset cleared reveals and prior-round feed visibility while retaining match kills.
- Results: Play_Hits.json and Play_Final.json.

The play-check setup initially accessed GameScene services before the scene load completed on the following frame. Setup was continued in the same play session. The automatic round bootstrap emitted its existing 30-second no-players setup error before the scripted rig took control; this was a test-harness preparation issue. No second play session was started. Existing edit-time multiplayer scenario-name conflict logs also remain outside this feature's scope.

After stopping play, MenuLobbyScene was restored, with one scene loaded, no unsaved changes, no compilation in progress and no preview-camera override.

## Captures

- Outline_Visible.png / Outline_Hidden.png / Outline_Partial.png
- Skinned_Name_Visible.png / Skinned_Name_Hidden.png / Skinned_Name_Partial.png
- HUD_Scoreboard.png / HUD_KillFeed_Penalty.png
- Play_Alive.png / Play_Spectator.png

## Remaining verification scope

No external Relay client, multiplayer build, network-latency calibration, or GPU timing benchmark was run. All peers must use the updated score message format and network-behaviour layout.