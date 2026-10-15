# Compact UI implementation — 2026-10-14

User approved UI simplification and requested collapsible instructions plus official Unity CLI use.

- Menu: controls start expanded with a clickable fold button. Tab, Esc, sensitivity and H are discoverable before starting. Fold state survives lobby entry/exit.
- Menu/lobby: resized for 1366×768; normalized the old 0.8 panel scale; retained eight slots, full nicknames, host/client rules and hidden-element restoration. Null ping entries render safely.
- HUD: one round/alive/spectating status strip, compact numeric ammunition with low-ammo colors, contextual bottom toast. Removed repeated weapon caption, bullet ticks, room code and always-visible full help.
- H toggles the optional in-game guide. Small H/Tab/Esc hints stay visible in action; cursor lock does not prevent H access.
- Tab shows eight dedicated ranking rows with full names and a local-player highlight. Countdown and winner notices take precedence; no automatic combat ranking panel.
- Shared visuals: neutral thin borders and almost square panels; disabled repeated cyan rails/feet/edges and button scaling. Gold remains for primary actions, rank emphasis and low ammo.
- ESC and podium headings/cards are smaller; duplicate captions removed. Existing explicit build-safe podium material is preserved.
- Authoring is saved in MenuLobbyScene and GameScene. PlayerSettings default window and canvas references now use 1366×768. Future generation code matches saved layout.
- AGENTS.md records the user's preference for official `unity` CLI and the connected editor.

Verification:
- Current editor compiled the source successfully. CLI recompile requests later returned HTTP 400 during automatic refresh; CLI eval confirmed isCompiling=false and scriptCompilationFailed=false, and Assembly-CSharp.dll was newer than the final source changes.
- CLI-driven authored preview checks passed at 1366×768, 1280×720 and 1920×1080: 8-person roster, 20-character Korean/wide Latin names, host/client start, fold/room-return, text bounds and all HUD state combinations.
- ESC and podium text bounds and captures passed at 1366×768.
- Live Play mode used the real CanvasScaler: Screen=1366×768, scaleFactor=1. Frames advanced from 15508 to 16155; guide button folded successfully and room return retained fold state. Play mode stopped afterward and temporary background override restored.
- Captures and detailed checks: Assets/HideAndShoot/QA/CompactUI. Live_Menu.png is a real Game View capture; HUD state images are authored-layout previews rather than an online Relay match.
- No new Windows build was run for this UI change. Existing standalone binaries must be rebuilt. The prior podium shader fix already passed a separate release player render check.

Reapply/verify with official CLI:
`unity command run_script AgentScripts/Ui1366.cs Ui1366.Apply --project-path C:/UnityProject/HideAndShoot`
`unity command run_script AgentScripts/Ui1366.cs Ui1366.Verify --project-path C:/UnityProject/HideAndShoot`
`unity command run_script AgentScripts/Ui1366.cs Ui1366.CapturePodium --project-path C:/UnityProject/HideAndShoot`
