# HypeReborn
An open source remake of Hype: The Time Quest in Godot (C#).

The following elements are planned in the order listed:
- Original game map scenes, gameplay, and assets.
- Player character and controls.
- Multiplayer support.
- Custom zones & gameplay extensions.
- Remastered art assets & switch between original and remastered visuals.

NOTE THAT AN ORIGINAL GAME COPY MUST BE INSTALLED TO RUN THIS PROJECT.
While entirely custom, the application uses the original map data, assets, scripts, and audio from the official game, and we will never provide these.

## Current State
- External game assets are resolved from a legit Hype install path (not imported into `res://`).
- Runtime entrypoint is `res://Scenes/HypeMainMenu.tscn` (permanent main scene).
- Main menu visuals are loaded from original CNT assets (`MENU/menu_princ` and `FixTex/menus/mainmenu`).
- A custom Godot editor dock (`Hype Browser`) indexes maps, script sources, and animation-bank sources.
- One Godot scene file exists per Hype level under `Scenes/Hype/`.
- Each map scene uses resolver nodes (`HypeMapRoot` and `HypeResolvedObject3D`) so designers can edit modern gameplay/layout while keeping original data linked.

## Editor Quick Start
1. Open the project in Godot with C# enabled.
2. In the `Hype Browser` dock, set the external Hype install root (`.../Game` or its parent).
3. Click `Refresh` to index assets.
4. Click `Generate Map Scenes` to (re)create one Godot scene per level.
5. Open any scene in `Scenes/Hype/` and use `Rebuild Open Map` after changing map definition settings.

## Player Sandbox
- Prototype scene: `res://Scenes/Player/PlayerSandbox.tscn`
- Reusable player scene: `res://Scenes/Player/HypePlayer.tscn`
- Character visual source: parsed from original Hype level data at runtime (no placeholder fallback)
- Actor system: scans Montreal Perso actors across levels, classifies humanoid candidates, and persists selected player actor in `user://hype_player_actor_state.json`
- Debug picker: runtime actor dropdown in the player debug overlay lets you swap to any discovered actor and persists selection
- First-step controls: `WASD`, mouse look, `Shift` sprint, `Space` jump, `E` interact placeholder, `Esc` mouse capture toggle.
