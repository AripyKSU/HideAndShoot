# UI and podium polish — 2026-10-13

- Menu, lobby, HUD, ESC menu and podium share dark chamfered panels, cyan/gold accents, stronger typography and button feedback. The ESC menu uses UGUI rather than the temporary IMGUI layout.
- The lobby uses eight fixed member rows. Names, host badges and Ping occupy separate columns. Full 20-character Korean and wide Latin names fit without shortening. Extremely large Ping values display as `999+ ms`. Host/client start-button rules are preserved.
- Four distinct existing Mixamo victory motions are exported as looping native `.anim` assets under `Resources/PodiumAnimations`. The three podium ranks use different clips, with the selection varying deterministically between match results. The presentation plays the full body and hides pistols and pistol-specific finger poses.
- The podium adds rank-colored stage accents and separate champion, runner-up and third-place cards. Existing two-player matches keep the third place vacant.

## Verification

See `Assets/HideAndShoot/QA/UiPodiumRegression.txt` and the associated PNGs. The editor preview checks eight maximum-length names at 1920×1080, 1280×720 and 1280×1024, host/client button states, the eight-player scoreboard, distinct animation curves and moving podium poses. These are rendered preview checks, not eight live Relay clients.

Rerun with **Tools > HideAndShoot > QA > Verify Styled UI and Eight-player Lobby**. Native animation assets can be rebuilt with **Tools > HideAndShoot > Install Diverse Podium Animations**. The original imported FBX assets and saved gameplay scenes are preserved.
