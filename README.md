# HypeReborn
An open source remake of Hype: The Time Quest in Godot (C#).

## Current State
- External game assets are resolved from a legit Hype install path (not imported into `res://`).
- Runtime entrypoint is `res://Scenes/HypeMainMenu.tscn` (permanent main scene).
- Main menu visuals are loaded from original CNT assets (`MENU/menu_princ` and `FixTex/menus/mainmenu`).
- A custom Godot editor dock (`Hype Browser`) indexes maps, script sources, and animation-bank sources.
- One Godot scene file exists per Hype level under `Maps/Hype/`.
- Each map scene uses resolver nodes (`HypeMapRoot` and `HypeResolvedObject3D`) so designers can edit modern gameplay/layout while keeping original data linked.

## Editor Quick Start
1. Open the project in Godot with C# enabled.
2. In the `Hype Browser` dock, set the external Hype install root (`.../Game` or its parent).
3. Click `Refresh` to index assets.
4. Click `Generate Map Scenes` to (re)create one Godot scene per level.
5. Open any scene in `Maps/Hype/` and use `Rebuild Open Map` after changing map definition settings.
