# Podium build material fix — 2026-10-14

The podium created runtime cubes and cloned renderer.sharedMaterial. URP 17.3 supplies its default material only in the Editor (UniversalRenderPipelineAsset.DefaultResources.cs); in a player the same primitive used Hidden/InternalErrorShader. The old Block method therefore copied an error material into every podium block.

MatchPodiumPresenter now loads Resources/PodiumStageMaterial once before creating the stage, checks availability/support, and clones that explicit URP Lit asset for each block. The material has _EMISSION enabled in the saved asset so build shader stripping sees the same variant used at runtime. Block changes emission color without changing shader keywords. Runtime clones are destroyed by the existing cleanup; the shared asset is preserved.

Validation used an independent copy of the project because the active Unity Editor's connector was disconnected. Original gameplay scenes were not changed. A Windows build with BuildOptions.None (release settings) included the ordinary menu/game scenes plus opt-in QA code through extraScriptingDefines. The QA code is excluded from normal builds.

Results:
- C# compilation succeeded.
- Windows build succeeded with 0 errors and 10 existing warnings (mainly deprecated TMP wrapping/other existing project warnings).
- Player ran on Direct3D 12 / NVIDIA RTX 4060 Laptop GPU and exited with code 0.
- Original primitive material shader: Hidden/InternalErrorShader (root cause reproduced in the player).
- All 10 stage blocks used supported Universal Render Pipeline/Lit with _EMISSION enabled.
- Authored result overlay was active.
- 1366×768 3D render: 153616 visible pixels, 0 error-magenta pixels; image inspected visually.

Artifacts: Builds/QA_Podium/BuildReport.txt, PodiumBuildRegression.txt, PodiumBuild_1366x768.png, Player.log.

Rerun: Tools > HideAndShoot > QA > Build Podium Shader Regression, then run Builds/QA_Podium/HideAndShoot.exe with -batchmode -screen-fullscreen 0 -screen-width 1366 -screen-height 768 -hs-podium-qa.

The capture isolates the 3D stage; screen-space result text is checked for activation, not rendered into that capture. Online Relay flow was not exercised. Existing Builds/1.0 and other release folders were not overwritten; rebuild the intended release to incorporate the source fix.
