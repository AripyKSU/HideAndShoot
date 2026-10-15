# Relay/NGO 2-client integration QA — 2026-10-12

## Confirmed by real runtime logs
- Editor Host (PID 33156) connected to Unity Gaming Services; Relay join code created.
- Standalone development build: Result=Succeeded, Errors=0, Warnings=1.
- Standalone headless Client (test process PID 2248) joined the real Relay room.
- Client received Host-driven network roster repeatedly with Count=2.
- Host Play became enabled at two players.
- Host initiated NGO NetworkSceneManager.LoadScene("GameScene", Single).
- Host entered GameScene (Host NGO GameScene is active).

## Not yet confirmed
- Client actually entered GameScene. Earlier client build lacked the OnSceneLoaded diagnostic,
  so absence of a load log does not prove failure. Must retest with new build.
- Actual RTT value shown in right aligned Ping text and Host disconnection UI.

## Error found and fix awaiting rebuild
- When Host Play ended and Host test exited, Client disconnected.
- Client OnClientDisconnected called MenuLobbyUI.HideRoom on a UI whose scene was already destroyed.
  Stacktrace: UnityEngine.UI.Selectable.DoStateTransition -> MenuLobbyUI.SetBusy -> HideRoom.
- Fixed source in Scripts/Networking/RelayLobbySession.cs:
  * Compare menu instances with ReferenceEquals rather than Unity's overloaded destroyed-object equality.
  * Reset stale menu ref in UnbindMenu and guard calls with (menu != null), not null-conditional operator.
  * Defer local MainMenu scene load to the next frame after NGO disconnect callback.
  * Add OnSceneLoaded diagnostic and Client roster RTT log.
- This fix is source-written but NOT yet compile/build verified. Do not call it resolved.

## Safety and reproduction
- User-requested Unity Editor remains open (PID 33156). No process exit/restart.
- Test standalone Client PID 2248 explicitly stopped after test so no leaked headless client.
- Build request for incremental player build is queued under Temp and consumed by the live editor,
  but the Editor has not yet processed it while unfocused. Recompile after focusing Unity.
- Last verified player build artifact: Builds/QA_Standalone/HideAndShoot.exe (older source).
- Edit-mode UI QA: 16 PASS, 0 FAIL (QA/LobbyEditModeAudit.txt).
