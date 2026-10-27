# Unity workflow
Use the official Unity CLI (`unity`, installed in the user's Unity/bin directory) for editor interaction, scene/asset edits, compilation and captures. The user explicitly requested this on 2026-10-14. Prefer the connected editor for this project and pass `--project-path C:/UnityProject/HideAndShoot`. Read command schemas before invoking them; preserve unsaved scene changes. Do not create a separate copied project for routine verification when the live CLI connection is available.

Batch Unity CLI operations into as few requests as practical to conserve credits. Finish implementation and static checks first; run Play mode verification only at the end. The user explicitly requested this on 2026-10-16.

