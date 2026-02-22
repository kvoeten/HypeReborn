# Hype: The Time Quest — Engine Architecture Analysis

> **Binary**: `Hype.exe` (Win32 PE, x86)
> **Engine**: Ubisoft Montpellier proprietary engine (shared lineage with Rayman 2 / Tonic Trouble)
> **Analysis Date**: 2026-02-21

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Engine Bootstrap & Threading Model](#engine-bootstrap--threading-model)
3. [Game State Machine & Gameflow](#game-state-machine--gameflow)
4. [The Frame Tick — Render & Simulation Loop](#the-frame-tick--render--simulation-loop)
5. [Scene Graph — The SuperObject Hierarchy (HIE_)](#scene-graph--the-superobject-hierarchy-hie)
6. [Actor Model — "Perso" and Families](#actor-model--perso-and-families)
7. [AI & Scripting System — Intelligence, Reflexes, Comports](#ai--scripting-system--intelligence-reflexes-comports)
   - [The Mind Architecture](#the-mind-architecture)
   - [Designer Variables (DsgVar)](#designer-variables-dsgvar)
   - [The Comport / Rule / Node Tree Interpreter](#the-comport--rule--node-tree-interpreter)
   - [Scripts: Conditions, Functions, Procedures](#scripts-conditions-functions-procedures)
   - [State Machine & Actions](#state-machine--actions)
8. [Character Abilities & Gameplay Mechanics](#character-abilities--gameplay-mechanics)
   - [Movement & Physics](#movement--physics)
   - [Combat — Hit Points, Power, Fire](#combat--hit-points-power-fire)
   - [Magic System (Points de Magie)](#magic-system-points-de-magie)
   - [Inventory System (INV_)](#inventory-system-inv)
   - [Swimming & Environment Interactions](#swimming--environment-interactions)
   - [Capabilities System](#capabilities-system)
9. [Collision System (COL_)](#collision-system-col)
   - [Zone Types: ZDD, ZDE, ZDM](#zone-types-zdd-zde-zdm)
10. [Dynamics & Physics Engine (DNM_)](#dynamics--physics-engine-dnm)
11. [Graphics Layer (GLI_) & Rendering Pipeline](#graphics-layer-gli--rendering-pipeline)
12. [Camera System (CAM_)](#camera-system-cam)
13. [Waypoint & Navigation (WP_ / WPG_)](#waypoint--navigation-wp--wpg)
14. [Dialog System (DLG_)](#dialog-system-dlg)
15. [Sound System (SND_)](#sound-system-snd)
16. [Input System (IPT_)](#input-system-ipt)
17. [Menu System (MNU_)](#menu-system-mnu)
18. [Sector System (SECT_ / SCT_)](#sector-system-sect--sct)
19. [Additional Subsystems](#additional-subsystems)
20. [Compatibility Issues & DRM Protection](#compatibility-issues--drm-protection)
    - [CD-ROM Disc Check (DRM)](#cd-rom-disc-check-drm)
    - [Ubi.ini Configuration System](#ubiini-configuration-system)
    - [DirectX & Display Compatibility](#directx--display-compatibility)
    - [Patching Guide](#patching-guide)
21. [Particularities & Observations](#particularities--observations)

---

## Executive Summary

Hype: The Time Quest (2000, Ubisoft Montpellier / Playmobil Interactive) runs on a **proprietary C engine** with a strict modular prefix convention. Every subsystem exposes its public API through a `PREFIX_fn_` naming scheme (e.g., `HIE_fn_`, `COL_fn_`, `GLI_fn_`). The game is a single-threaded game loop running on a dedicated worker thread, with the main Win32 thread acting solely as a message pump.

The engine features:
- A **SuperObject scene graph** with matrix stacks and hierarchical transforms
- A **data-driven AI system** with dual-layer Intelligence + Reflex behaviors, interpreted from compiled scripts
- A rich **scripting language** with Conditions, Functions, and Procedures that cover every aspect of gameplay
- **Frame-based timing** with delta-time support for physics and animation
- A **game state machine** with 40+ discrete states governing the lifecycle from boot logo to in-game to cinematic sequences

---

## Engine Bootstrap & Threading Model

### WinMain (0x402A70)

The entry point follows a standard Win32 pattern with some notable features:

```
WinMain
  ├── CreateMutexA("Hype") → single-instance enforcement
  ├── sub_4025B0()          → pre-initialization / config validation
  ├── fopen(logFile)        → opens debug log file
  ├── GetCurrentThreadId()  → stored for thread identification
  ├── strcpy(cmdLine)       → preserves command-line args
  ├── sub_402180()          → internal setup (debug/config)
  ├── sub_402BB0()          → Win32 window creation (RegisterClass, CreateWindowEx)
  ├── sub_402230()          → GLI graphics subsystem initialization
  ├── sub_402600()          → Creates the game engine thread
  └── Win32 Message Loop    → GetMessage / TranslateMessage / DispatchMessage
```

**Key Design Decision**: The main thread **never** runs game logic. It exists purely for the Win32 window message pump. Game logic runs on a **high-priority worker thread** created via `CreateThread` → `StartAddress` (0x402630).

### Game Engine Thread Entry (0x402630 → 0x402340)

```c
StartAddress() → sub_402340()

sub_402340() {
    sub_402260();    // one-time game initialization (thunk → sub_402390)
    
    while (!ShouldQuitGame()) {             // outer loop: per-level
        InitializeLevel();                  // sub_402290 — sets up waypoint callbacks, loads level
        while (!ShouldEndLevel()) {         // inner loop: per-frame
            WaitForFrameSync();             // sub_402400 — WaitForMultipleObjects
            ProcessFrame();                 // sub_406D00 — render, input, game tick
        }
        CleanupLevel();                     // sub_4022B0 → sub_406A30
    }
    
    sub_402710(32);     // final cleanup
    SetEvent(hEvent);   // signal main thread
    Sleep(INFINITE);    // park thread
}
```

The outer loop iterates **per level/map**. The inner loop processes **per frame**. Frame sync is achieved via `WaitForMultipleObjects` on two handles: a frame semaphore and a quit event.

---

## Game State Machine & Gameflow

### State Variable: `dword_727680`

The centerpiece of the game's flow control is a massive `switch` statement in `sub_4062F0` that dispatches on a global state variable. States transition via `sub_406DA0(nextState)`.

| State | Description |
|-------|-------------|
| 1     | **New Game / Initial Load** — Falls through to state 10 |
| 3     | **Level Load (Standard)** — Cursor setup, waterplane on, collision setup, sound init |
| 5     | **Level Load (Alternate)** — Similar to 3 with different camera/collision config |
| 7     | **Minimal Load** — Lightweight level transition |
| 9     | **In-Game (Active Gameplay)** — Target of most transitions; sets render callbacks |
| 10    | **World Data Load** — Loads "Hype" world, binary textures, initializes waypoints |
| 14    | **Input Reading A** — Reads input, transitions to 16 |
| 15    | **Input Reading B** — Reads input, transitions to 17 |
| 18    | **Dialog-Active State** — Enables dialog timer |
| 22    | **Level Cleanup / Transition** — Waterplane off, memory cleanup |
| 24    | **Level Transition** — Cursor setup, can branch to state 4 or state 37 |
| 25    | **Cinematic A** — Loads cutscene data, sets cinematic render callback |
| 27    | **Collision Init / Transition** — Sets up collision, transitions to 9 |
| 28    | **Cinematic B** — Another cutscene loading path |
| 30    | **Collision Init (Alternate)** — Same as 27 |
| 31    | **Logo Video** — Plays `Ubilogo.avi`, transitions to 33 |
| 33    | **Post-Logo** — Sets in-game render, transitions to 9 |
| 34    | **Special Load** — Another loading path |
| 37    | **Sequence Load** — Init sequence data, transition to 38 |
| 39    | **Post-Sequence** — Checks waterplane state, returns to 9 |
| 40    | **Special Sequence** — Loads special data, transitions to 41 |
| 42    | **Final Transition** — Sets waterplane on, sets render, transitions to 9 |

### Render Callback System

The game uses two function pointers to decouple rendering from state logic:

- **`off_5BA258`** — Primary scene render function (world geometry, actors, effects)
- **`off_5BA25C`** — Overlay/secondary render function (HUD, debug waypoints)

During gameplay (state 9), these are set to:
- `sub_44E450` — Full scene tick and render
- `sub_44E8E0` — Overlay rendering (HUD, inventory display)

During cinematics or transitions, they may be replaced with specialized renderers.

---

## The Frame Tick — Render & Simulation Loop

### sub_44E450 — The Core Game Tick

This function is the heart of every in-game frame:

```
sub_44E450()
  ├── sub_42E960()                      → Pre-frame cleanup
  ├── sub_4DEFE0()                      → Sound system tick
  ├── SHW_fn_vInitShadowCounter()       → Reset shadow count
  ├── HIE_gs_lCurrentFrame++            → Increment global frame counter
  ├── sub_435D90()                      → Pre-hierarchy update
  │
  ├── [if not cinematic mode]:
  │   ├── sub_431AA0()                  → Process actors / AI for this frame
  │   ├── Waypoint dynamic connections  → Update dynamic line waypoints
  │   ├── sub_448FD0()                  → Post-actor processing
  │   ├── POS_fn_vSetIdentityMatrix()   → Reset matrix stack
  │   ├── sub_44E200(rootObj)           → Recursive scene graph traversal + transform computation
  │   ├── off_5CE0A8(rootObj, 0)        → Object rendering callback
  │   ├── sub_4485B0()                  → Post-render processing
  │   ├── sub_4505B0()                  → Mouse cursor rendering (if enabled)
  │   ├── sub_44D020()                  → Shadow rendering
  │   ├── sub_44D8F0()                  → Particle effects rendering
  │   ├── GLI_vRefreshAllCyclingTextures → Animate textures (time * 0.06)
  │   └── sub_41D6E0()                  → Additional per-frame effects
  │
  ├── [else - cinematic mode]:
  │   ├── HIE_fn_vRefreshHierarchy()    → Simplified hierarchy update  
  │   └── Reset matrix stack
  │
  ├── WaitForSingleObject(vsync)        → Wait for display sync
  ├── sub_469DB0()                      → Display buffer swap / present
  ├── GLI_xClearViewingList()           → Clear render list for next frame
  ├── sub_46B090()                      → Viewport calculation
  └── SCT_fn_vSendSectorWhereIAm...()  → Sector visibility with mirror support
```

**Frame Counter**: `HIE_gs_lCurrentFrame` is a monotonically increasing integer that wraps from -1 back to 0. It drives animation timing, LOD decisions, and cache invalidation.

---

## Scene Graph — The SuperObject Hierarchy (HIE_)

The engine's spatial organization is built on **SuperObjects** — a tree structure where each node has:

| Field | Accessor | Purpose |
|-------|----------|---------|
| Father | `HIE_fn_hGetSuperObjectFather` | Parent node |
| FirstChild | `HIE_fn_hGetSuperObjectFirstChild` | First child |
| NextBrother | `HIE_fn_hGetSuperObjectNextBrother` | Sibling |
| GlobalMatrix | `HIE_fn_hGetSuperObjectGlobalMatrix` | World-space transform |
| LocalMatrix | `HIE_fn_hGetSuperObjectMatrix` | Local transform |
| Object | `HIE_fn_hGetSuperObjectObject` | Attached game object |
| Type | `HIE_fn_lGetSuperObjectType` | Object type enum |
| DrawMask | `HIE_fn_lGetSuperObjectDrawMask` | Visibility flags |
| BoundingVolume | `HIE_fn_hGetSuperObjectBoundingVolume` | Culling bounds |
| Light | `HIE_fn_hGetSuperObjectLight` | Attached light |
| TransparenceLevel | `HIE_fn_fGetSuperObjectTransparenceLevel` | Alpha value |

### Matrix Stack

The engine maintains a **matrix stack** (analogous to OpenGL's deprecated `glPushMatrix` / `glPopMatrix`):

- `HIE_fn_vInitMatrixStack` — Initialize stack with identity
- `HIE_fn_vPushMatrix` / `HIE_fn_vPopMatrix` — Stack operations during hierarchy traversal
- `HIE_fn_vLoadMatrix` / `HIE_fn_vLoadIdentity` — Direct matrix loads
- `HIE_g_a_hMatrixStack` — The actual stack array
- `HIE_g_lNbMatrixInStack` — Stack depth counter
- `HIE_g_hCurrentMatrix` — Current top-of-stack pointer

This enables efficient recursive transform computation during the hierarchy traversal pass.

---

## Actor Model — "Perso" and Families

Game actors are called **"Perso"** (from French *personnage* — character). Every Perso belongs to a **Family**, which defines its animation channels, visual sets, collision sets, and behavior templates.

### Family Structure

From `Family.c`:
- Families define animation **channels** (max 254 per family)
- Each family has associated visual sets, physical objects, and collision sets
- Families serve as templates — multiple Perso instances can share a Family

### Perso Composition

A Perso is composed of:
1. **SuperObject** — Position in the scene graph
2. **StdGame data** (from `StdGame.c`) — Hit points, custom bits, game flags
3. **Brain** (from `Brain.c`) — AI / scripting attachment point
4. **3D Data** (from `3dData.c`) — Visual representation, channels
5. **Dynamics** (from `Dynam.c`) — Physics body, mechanical ID card
6. **Collision Set** (from `CollSet.c`) — Collision zones configuration
7. **Animation Effects** (from `AnimEff.c`) — Particle sources, sound triggers per animation
8. **MS_World / MS_Micro** — World/micro waypoint connections

---

## AI & Scripting System — Intelligence, Reflexes, Comports

This is the most architecturally rich system in the engine. It implements a fully data-driven behavioral scripting system that controls all NPC and player character behavior.

### The Mind Architecture

Every Perso with behavior has an **AI_tdstMind_** structure containing:

```
AI_tdstMind_
  ├── AI_tdstAIModel_          → Shared behavior model (template)
  ├── tdstIntelligence_ [2]    → [0] = Intelligence (normal behavior)
  │                              [1] = Reflex (interrupt behavior)
  ├── tdstDsgVar_*             → Designer variables array
  ├── tdstDsgMem_*             → DsgVar memory buffer
  └── tdstDsgVarInfo_*         → DsgVar metadata
```

The **AI Model** (`AI_tdstAIModel_`) is the template shared by all instances of a family. It defines the available Comports, Rules, and the structure of the behavior trees. Individual minds get their own Intelligence and DsgVar state.

### Intelligence vs. Reflex — Dual-Layer Behavior

The engine runs **two parallel behavior layers** per Perso:

| Layer | Structure | Purpose |
|-------|-----------|---------|
| Intelligence | `tdstIntelligence_[0]` | Normal behavior — walking, idle, patrolling, interacting |
| Reflex | `tdstIntelligence_[1]` | Interrupt behavior — collision reactions, taking damage, falling |

**Reflex always takes priority over Intelligence.** When a reflex fires, it preempts the current intelligence behavior. The string `InitComportIntelligence` and `InitComportReflex` confirm separate initialization paths.

### Designer Variables (DsgVar)

DsgVars are **typed, per-instance variables** that scripts can read and write. They are the primary mechanism for inter-script communication and state persistence.

Each DsgVar has:
- A **type** (`tdeDsgVarTypeId_`) — mapped from script names via `fn_eFindDsgVarTypeIdFromScriptName`
- A **current value** (`fn_vInitDsgVar`)
- An **initial value** (`fn_vInitDsgVarInit`) — for respawn/reset
- A **default value** (`fn_vInitDsgVarDefault`) — fallback

DsgVars are allocated per-mind via `fn_vAllocVariableDesigner`, sized according to type via `fn_ulSizeOfDsgVar`. The `.CAR` and `.DEC` file comparison error messages reveal that DsgVars are defined in configuration files and validated at load time.

### The Comport / Rule / Node Tree Interpreter

Behavior is organized hierarchically:

```
Intelligence / Reflex
  └── Current Comport (behavior state)
       └── Rules (ordered list)
            └── Node Tree (condition/action tree)
                 ├── Condition nodes (evaluate to true/false)
                 ├── Function nodes (compute values)
                 └── Procedure nodes (execute side effects)
```

**Comports** are named behavior states (e.g., "Idle", "Patrol", "Attack"). The strings `CreateComport`, `ComportRef`, `InitComportIntelligence`, `InitComportReflex` confirm this.

**Rules** are evaluated in order within a Comport. Each rule has a condition tree and an action tree. From the debug strings:
- `"Number of rules in this comport (comport n. %d) should be (%d)"`
- `"Depth undefined (node %d comport n. %d)"`
- `"Problem (schedule comport n. %d)"`

**Node Interpretation** is performed by `tdstNodeInterpret_` structures initialized via `fn_vInitNodeInterpret(node, value, type, param)`. The type enum `tdeTypeInterpret_` distinguishes between condition evaluation, function computation, and procedure execution.

### Comport Transitions

Scripts can change behavior at runtime:
- `Proc_ChangeComport` — Change another Perso's intelligence comport
- `Proc_ChangeComportReflex` — Change another Perso's reflex comport
- `Proc_ChangeMyComport` — Change own intelligence comport
- `Proc_ChangeMyComportReflex` — Change own reflex comport
- `Cond_IsInComport` — Check if a Perso is in a specific intelligence comport
- `Cond_IsInReflex` — Check if a Perso is in a specific reflex comport

### Scripts: Conditions, Functions, Procedures

The scripting language is built on three primitive types, each resolved from string names at load time:

#### Conditions (Cond_) — Boolean evaluators

| Category | Examples |
|----------|----------|
| **Logic** | `Cond_And`, `Cond_Or`, `Cond_Not` |
| **Comparison** | `Cond_Equal`, `Cond_Different`, `Cond_Lesser`, `Cond_Greater`, `Cond_LesserOrEqual`, `Cond_GreaterOrEqual` |
| **Input** | `Cond_PressedBut`, `Cond_JustPressedBut`, `Cond_ReleasedBut`, `Cond_JustReleasedBut` |
| **Collision ZDD** | `Cond_CollidePersoZDDWithPerso`, `Cond_CollideModuleZDDWithPerso`, `Cond_CollidePersoZDDWithAnyPerso`, `Cond_CollidePersoAllZDDWithPersoAllZDD` |
| **Collision ZDE** | `Cond_CollidePersoZDEWithPersoZDE`, `Cond_CollideModuleZDEWithPersoZDE`, `Cond_CollidePersoTypeZDEWithPersoTypeZDE`, `Cond_CollidePersoAllZDEWithPersoAllZDE` |
| **Collision ZDM** | `Cond_CollideMovingZDM`, `Cond_ZDMCollideWithGround`, `Cond_ZDMCollideWithWall`, `Cond_ZDMCollideWithSlope`, `Cond_ZDMCollideWithCeiling`, `Cond_ZDMCollideWithObstacle` |
| **Terrain** | `Cond_CollideWithGround`, `Cond_CollideWithWall`, `Cond_CollideWithSlope`, `Cond_CollideWithAttic`, `Cond_CollideWithCeiling`, `Cond_CollideWithNothing` |
| **Environment** | `Cond_InEnvironmentAir`, `Cond_InEnvironmentWater`, `Cond_InEnvironmentFire` |
| **Water** | `Cond_CanSwim`, `Cond_CanSwimOnSurface`, `Cond_CanSwimUnderWater`, `Cond_IsNotOutOfDepth`, `Cond_IsCompletelyOutOfWater` |
| **Vision** | `Cond_SeePerso`, `Cond_SeePosition`, `Cond_SeePersoWithOffset`, `Cond_SeePositionWithOffset` |
| **Spatial** | `Cond_IsAtLeftOfPerso`, `Cond_IsAtRightOfPerso`, `Cond_IsBehindPerso`, `Cond_IsInFrontOfPerso`, `Cond_IsAbovePerso`, `Cond_IsBelowPerso` |
| **Movement** | `Cond_InTopOfJump`, `Cond_IsThereMechEvent` |
| **Capabilities** | `Cond_HasTheCapability`, `Cond_PersoHasTheCapability`, `Cond_HasOneOfTheCapabilities`, `Cond_HasTheCapabilityNumber` |
| **Inventory** | `Cond_Inv_InventoryIsFull`, `Cond_Inv_FindObject` |
| **Dialog** | `Cond_GetDialogStatus`, `Cond_DLG_IsDialogOver`, `Cond_DLG_IsScrollingOver` |
| **Combat** | `Cond_TestPower`, `Cond_IsActivable`, `Cond_CollisionSphereSphere` |
| **Action** | `Cond_IsInAction`, `Cond_ChangeActionEnable`, `Cond_ActionFinished`, `Cond_GetAction` |
| **Materials** | `Cond_IsTypeOfGMTCollide`, `Cond_IsTypeOfGMTCollide_Wall`, `Cond_IsTypeOfGMTCollide_Obstacle` |
| **Sound** | `Cond_SND_IsSonFinished`, `Cond_LSY_IsSpeechOver` |
| **Camera** | `Cond_Camera_IsCamReachedItsOptPos`, `Cond_Camera_IsCamInAlphaOrientation`, `Cond_Camera_IsCamInTetaOrientation` |
| **Sector** | `Cond_SectorActive`, `Cond_IsSectorInTranslation`, `Cond_IsSectorInRotation` |
| **Validation** | `Cond_IsValidObject`, `Cond_IsValidWayPoint`, `Cond_IsValidGMT`, `Cond_IsValidAction`, `Cond_IsValidWay` |
| **Misc** | `Cond_IsCustomBitSet`, `Cond_NullVector`, `Cond_GiBlock`, `Cond_UserEvent_IsSet`, `Cond_IsPersoLightOn` |

#### Functions (Func_) — Value Computers

| Category | Examples |
|----------|----------|
| **Position** | `Func_GetPersoAbsolutePosition`, `Func_GetWPAbsolutePosition`, `Func_GetModuleAbsolutePosition`, `Func_GetModuleRelativePosition` |
| **Distance** | `Func_DistanceToPerso`, `Func_DistanceX/Y/ZToPerso`, `Func_DistanceXY/XZ/YZToPerso`, `Func_DistanceToWP`, `Func_DistanceToPosition`, `Func_DistanceToPersoCenter` |
| **Angles** | `Func_GetAngleAroundZToPerso`, `Func_GetAngleAroundZToPosition`, `Func_GetAlpha`, `Func_GetTheta`, `Func_GetAlphaPas`, `Func_GetThetaPas` |
| **Math** | `Func_Sinus`, `Func_Cosinus`, `Func_Square`, `Func_SquareRoot`, `Func_AbsoluteValue`, `Func_DegreeToRadian`, `Func_RadianToDegree`, `Func_Real`, `Func_Int` |
| **Random** | `Func_RandomInt`, `Func_RandomReal` |
| **Min/Max** | `Func_MinimumReal`, `Func_MaximumReal`, `Func_AbsoluteInteger` |
| **Hit Points** | `Func_GetHitPoints`, `Func_AddAndGetHitPoints`, `Func_SubAndGetHitPoints`, `Func_GetHitPointsMax`, `Func_AddAndGetHitPointsMax`, `Func_SubAndGetHitPointsMax` |
| **Magic Points** | `Func_LitPointsDeMagie`, `Func_LitPointsDeMagieMax`, `Func_AjouteEtLitPointsDeMagie`, `Func_EnleveEtLitPointsDeMagie` |
| **Time** | `Func_GetTime`, `Func_ElapsedTime`, `Func_GetDT` |
| **Lists** | `Func_ListSize`, `Func_GivePersoInList` |
| **Input** | `Func_GetInputAnalogicValue` |
| **Inventory** | `Func_Inv_AddObject`, `Func_Inv_RemoveObject`, `Func_Inv_GetGold`, `Func_Inv_AddGold`, `Func_Inv_SubGold`, `Func_Inv_GetWeapon`, `Func_Inv_ChangeWeapon`, `Func_Inv_GetMagic`, `Func_Inv_ChangeMagic`, `Func_Inv_GetObject`, `Func_Inv_SelectObject`, `Func_Inv_UseItem`, `Func_Inv_GetObjectQuantity`, `Func_Inv_GetObjectMaximumCapacity` |
| **Vectors** | `Func_GetNormSpeed`, `Func_GetVectorNorm`, `Func_AbsoluteVector`, `Func_RelativeVector`, `Func_VectorLocalToGlobal`, `Func_VectorGlobalToLocal`, `Func_MTH_CrossProduct`, `Func_MTH_NormalizeVector` |
| **Collision** | `Func_GetNormalCollideVector`, `Func_GetCollidePoint`, `Func_CollisionPoint`, `Func_CollisionNormalVector`, `Func_GetNormalGroundVector`, `Func_GetCurrentCollidedGMT`, `Func_GetLastTraversedMaterial`, `Func_LitDernierPersoCollisione` |
| **Colors** | `Func_ColorRGBA`, `Func_ColorRGB`, `Func_ColorRed/Green/Blue/Alpha`, `Func_AddColor`, `Func_AddRed/Green/Blue/Alpha` |
| **Materials (GMT)** | `Func_GetMechanicalGMT*Coef` (Adhesion, Absorption, Friction, Slide, Progression, Penetration), `Func_GetVisualGMT*` (Color, Specular, Diffuse, Ambient, TextureScrolling, Frame) |
| **Environment** | `Func_DepthEnvironment`, `Func_AltitudeEnvironment`, `Func_GetEnvironmentToxicity` |
| **Object Gen** | `Func_GenerateObject`, `Func_GetFather` |
| **Waypoint Network** | `Func_CloserWP`, `Func_DistanceCaracToWP`, `Func_ReseauLitIndexCourant`, `Func_ReseauLitPremierIndex`, `Func_ReseauCheminLePlusCourt` (shortest path!), `Func_NetworkAllocateGraphToMSWay`, `Func_NetworkWPCloserOrientation` |
| **Speed** | `Func_VitesseHorizontaleDuPerso`, `Func_VitesseVerticaleDuPerso`, `Func_SpeedChannel` |
| **Animation** | `Func_GetCurrectAnimFrame`, `Func_PositionAbsolueCanal` |
| **Proximity** | `Func_PersoLePlusProche`, `Func_PersoLePlusProcheAvecAngle` (nearest Perso with angle filter) |
| **Sound/Dialog** | `Func_SendSoundRequest`, `Func_SendVoiceRequest`, `Func_SendMusicRequest`, `Func_DLG_PersoTalks` |
| **Cursor** | `Func_PersoUnderCursor`, `Func_XCoordOfCursor`, `Func_YCoordOfCursor` |
| **Game State** | `Func_GetGameState`, `Func_ExecuteFloorGame` |
| **Collision Zones** | `Func_LitPositionZDM`, `Func_LitPositionZDE`, `Func_LitPositionZDD` |
| **Bounce** | `Func_CalculVecteurRebond`, `Func_CalculVecteurRebond2` |
| **Capabilities** | `Func_CollisionRopeSphere` |

#### Procedures (Proc_) — Side-Effect Actions

| Category | Examples |
|----------|----------|
| **Movement** | `Proc_SetNormSpeed`, `Proc_AddNormSpeed`, `Proc_MulNormSpeed`, `Proc_SetDirectionSpeed`, `Proc_SetVectorSpeed`, `Proc_GoRelative`, `Proc_GoInDirection`, `Proc_GoAbsoluteDirection`, `Proc_GoTarget`, `Proc_ReachTarget`, `Proc_Accelerate` |
| **Rotation** | `Proc_TurnLeft`, `Proc_TurnRight`, `Proc_TurnUp`, `Proc_TurnDown`, `Proc_TurnAround`, `Proc_Turn`, `Proc_Turn2`, `Proc_TurnPerso`, `Proc_DeltaTurnPerso`, `Proc_TurnAbsoluteDirection`, `Proc_SetRotationAxe`, `Proc_SetRotationAngleStep` |
| **Angles** | `Proc_SetAlphaAngle`, `Proc_SetThetaAngle`, `Proc_ResetOrientation` |
| **Impulse** | `Proc_SetImpulse`, `Proc_Pulse`, `Proc_StonePulse`, `Proc_SwimPulse` |
| **Jumping** | `Proc_Jump`, `Proc_JumpAbsolute`, `Proc_JumpWithoutAddingSpeed` |
| **Combat** | `Proc_Fire`, `Proc_SetPower`, `Proc_AddPower`, `Proc_SubPower`, `Proc_KillPerso` |
| **Hit Points** | `Proc_SetHitPoints`, `Proc_AddHitPoints`, `Proc_SubHitPoints`, `Proc_SetHitPointsToInit`, `Proc_SetHitPointsToMax`, `Proc_SetHitPointsMax` |
| **Braking** | `Proc_Brake`, `Proc_AccelTurbo`, `Proc_ResetSpeed` |
| **Lateral** | `Proc_LateralLeft`, `Proc_TurnLateralright`, `Proc_SkiTurnLeft`, `Proc_SkiTurnRight` |
| **Wind** | `Proc_AddWind` |
| **Target** | `Proc_SetTarget`, `Proc_SetDynamScalar` |
| **Object Lifecycle** | `Proc_ActivateObject`, `Proc_DesactivateObject`, `Proc_ActivateObjectOnPosition` |
| **Map/Level** | `Proc_ChangeMap`, `Proc_ChangeMapAtPosition`, `Proc_ChangeInitPosition` |
| **Player** | `Proc_SetMainActor`, `Proc_PlayerIsDead`, `Proc_PlayerIsDeadWithOption`, `Proc_PlayerIsDeadWithPlacement` |
| **Behavior** | `Proc_ChangeComport`, `Proc_ChangeComportReflex`, `Proc_ChangeMyComport`, `Proc_ChangeMyComportReflex` |
| **Actions** | `Proc_ChangeAction`, `Proc_ChangeActionRandom`, `Proc_SetActionReturn` |
| **Hierarchy** | `Proc_BecomesSonOfPerso`, `Proc_BecomesFatherOfPerso`, `Proc_FillListWithSons` |
| **Lists** | `Proc_AddPersoInList`, `Proc_ListAffectWithPersoZDD/ZDE`, `Proc_ListSort`, `Proc_ListSortByFamily`, `Proc_ListUnion/Inter/Diff/Add` |
| **Camera** | `Proc_Camera_UpdatePosition`, `Proc_Cam_ChangeTgtChannel`, `Proc_Cam_StopTargettingChannel` |
| **Speech** | `Proc_LSY_StartSpeech`, `Proc_LSY_StopSpeech` |
| **Module Control** | `Proc_TakeModuleControl`, `Proc_ReleaseModuleControl`, `Proc_InitModuleCtrlWithAnimTranslation/Rotation` |
| **Sector** | `Proc_RotateSector`, `Proc_RotateSectorLocalX/Y/Z`, `Proc_TranslateSector`, `Proc_TranslateLocalSector`, `Proc_LevelSaveRotationSector`, `Proc_PlayerSaveRotationSector` |
| **Save/Load** | `Proc_SaveGame`, `Proc_IncHistoricAndSaveGame`, `Proc_SaveAllGameValues` |
| **Input** | `Proc_EnableEscape`, `Proc_ResetButtonState`, `Proc_ActivateBut`, `Proc_DeactivateBut` |
| **Channel** | `Proc_ActivateChannel`, `Proc_DeactivateChannel` |
| **Fog** | `Proc_FogOn`, `Proc_FogOff`, `Proc_SetFogColor`, `Proc_SetFogNearFarInf` |
| **Lighting** | `Proc_PersoLightOn/Off`, `Proc_SetPersoLightColor`, `Proc_SetPersoLightNearFar`, `Proc_SetPersoLightGyrophare`, `Proc_SetPersoLightPulse`, `Proc_SetPersoLight*Type` (Parallel, Spherical, HotSpot, Ambient) |
| **Dynamic Light** | `Proc_DYL_ChangeGraduallyIntensity`, `Proc_DYL_CopyStaticToDynamic` |
| **Surfaces** | `Proc_AddSurfaceHeight`, `Proc_MoveSurfaceHeight`, `Proc_LevelSaveMovingSurface` |
| **Sound** | `Proc_SendSoundRequest`, `Proc_SendVoiceRequest`, `Proc_SendMusicRequest` |
| **Display** | `Proc_TransparentDisplay`, `Proc_DefautDisplay`, `Proc_SetTransparency`, `Proc_SetDisplayFixFlag`, `Proc_DisplaylValue`, `Proc_DisplayChrono`, `Proc_DisplayString`, `Proc_ActivateString`, `Proc_EraseString`, `Proc_DisplayVignetteDuring` |
| **Waypoint** | `Proc_ReInitWay`, `Proc_ReInitWayBack`, `Proc_InitWayWithWp` |
| **LOD** | `Proc_AllowDynamLOD`, `Proc_ForbidDynamLOD` |
| **Particles** | `Proc_SetParticleGeneratorOn/Off`, `Proc_SetGenerationMode*` (None, Continuous, Crenel, Probability), `Proc_SetGenerationNumber*` (Constant, Probabilist), `Proc_SetGeneration*LifeTime` (Infinite, Constant, Probabilist) |
| **Materials (GMT)** | `Proc_SetMechanicalGMT*Coef`, `Proc_SetVisualGMT*` |
| **Menu** | `Proc_StartMenuWithPauseGame`, `Proc_StartMenuWithoutPauseGame` |
| **Animation** | `Proc_FactorAnimationFrameRate` |
| **Capabilities** | `Proc_CapsGetCapabilities`, `Proc_CapabilityAtBitNumber` |
| **Misc** | `Proc_None`, `Proc_Select`, `Proc_UnSelect`, `Proc_SwapLinkTableObjects` |

### State Machine & Actions

Within each Comport, a **state machine** controls animation/action sequencing:

- **Action Table** (`tdstActionTable_` / `tdstActionTableEntry_`): Maps MetaActions to concrete animations
- **States**: Each state defines target states, next states, and prohibited transitions
- `SetState`, `NewState`, `CreateNewState`, `InitialState`, `NextState`, `AddTargetState`, `ProhibitedTargetState`
- `NoState_EndOfAction` — What happens when an action finishes without a defined next state

The error string `"WantedState == NULL, a state has ended and no new state has been positioned"` reveals that the engine requires explicit state transitions — there is no implicit default.

**MetaActions** are high-level action types (e.g., "Walk", "Run", "Attack", "Idle") that get resolved to concrete animation entries in the Action Table. Functions:
- `fn_eFindMetaActionIdFromScriptName` / `szFindMetaActionScriptNameFromId` — String ↔ ID conversion
- `szGetMetaActionTypeInParamFromId` — Get parameter type for a meta-action
- `fn_eGetNbMetaAction` — Total count of registered meta-actions

---

## Character Abilities & Gameplay Mechanics

### Movement & Physics

The character's movement is driven by a combination of script Procedures and the dynamics engine:

- **Speed**: `Proc_SetNormSpeed`, `Proc_AddNormSpeed`, `Proc_MulNormSpeed` — scalar speed control
- **Direction**: `Proc_GoRelative`, `Proc_GoInDirection`, `Proc_GoAbsoluteDirection` — directional movement
- **Vector Speed**: `Proc_SetVectorSpeed`, `Proc_SetDirectionSpeed`, `Proc_AddDirectionSpeed`
- **Turning**: `Proc_TurnLeft/Right/Up/Down/Around`, `Proc_Turn`, `Proc_TurnPerso` — rotation control
- **Impulse**: `Proc_SetImpulse` — instant velocity change
- **Jumping**: `Proc_Jump`, `Proc_JumpAbsolute`, `Proc_JumpWithoutAddingSpeed` — three jump variants
- **Braking**: `Proc_Brake`, `Proc_AccelTurbo` — deceleration and boost
- **Target Pursuit**: `Proc_GoTarget`, `Proc_ReachTarget`, `Proc_SetTarget`

The `Cond_InTopOfJump` condition enables scripts to detect the apex of a jump arc for timing attacks or double-jumps.

### Combat — Hit Points, Power, Fire

- **Hit Points**: Full CRUD via `Proc_Set/Add/SubHitPoints`, `Proc_SetHitPointsToInit/ToMax`
- **Power**: `Proc_SetPower`, `Proc_AddPower`, `Proc_SubPower`, `Cond_TestPower`
- **Firing**: `Proc_Fire` — projectile/attack action
- **Death**: `Proc_PlayerIsDead`, `Proc_PlayerIsDeadWithOption`, `Proc_PlayerIsDeadWithPlacement`
- **Kill**: `Proc_KillPerso` — instant NPC kill

### Magic System (Points de Magie)

The French naming reveals this is a first-class system:

- `Func_LitPointsDeMagie` — Read current magic points
- `Func_LitPointsDeMagieMax` — Read max magic points
- `Func_AjouteEtLitPointsDeMagie` — Add and return magic points
- `Func_EnleveEtLitPointsDeMagie` — Remove and return magic points
- `Func_AjouteEtLitPointsDeMagieMax` — Modify max magic capacity
- `Func_EnleveEtLitPointsDeMagieMax` — Reduce max magic capacity

Magic is separate from Hit Points, confirming the game has distinct health and mana pools.

### Inventory System (INV_)

A comprehensive inventory subsystem:

- **Objects**: `Func_Inv_AddObject`, `Func_Inv_RemoveObject`, `Func_Inv_GetObject`, `Func_Inv_SelectObject`, `Func_Inv_UseItem`
- **Gold**: `Func_Inv_GetGold`, `Func_Inv_AddGold`, `Func_Inv_SubGold`
- **Weapons**: `Func_Inv_GetWeapon`, `Func_Inv_ChangeWeapon`
- **Magic Items**: `Func_Inv_GetMagic`, `Func_Inv_ChangeMagic`
- **Quantities**: `Func_Inv_GetObjectQuantity`, `Func_Inv_RemoveObjectQuantity`, `Func_Inv_GetObjectMaximumCapacity`
- **Capacity Check**: `Cond_Inv_InventoryIsFull`, `Cond_Inv_FindObject`

Initialization functions: `INV_fn_vInitInventory`, `INV_fn_vInit`, `INV_fn_vInitLoad`, `INV_fn_vInitDrawInventory`

### Swimming & Environment Interactions

Rich water mechanics:

- `Cond_CanSwim` — General swim capability check
- `Cond_CanSwimOnSurface` — Surface swimming
- `Cond_CanSwimUnderWater` — Underwater swimming
- `Cond_IsNotOutOfDepth` — Depth check
- `Cond_IsCompletelyOutOfWater` — Full exit detection
- `Cond_InEnvironmentWater/Air/Fire` — Current environment type
- `Func_DepthEnvironment` — Current water depth
- `Func_AltitudeEnvironment` — Height above terrain
- `Func_GetEnvironmentToxicity` — Environmental damage
- `Proc_SwimPulse` — Swimming propulsion

The waterplane system (`GLI_vSetWaterplaneOn/Off`, `GLI_vWaterplaneEnableRefraction`, `VET_fn_vSetRefractionInWaterEffect`) confirms a dedicated water rendering pipeline with refraction effects.

### Capabilities System

A **bitmask-based capability system** controls what characters can do:

- `Cond_HasTheCapability` — Current Perso has capability by name
- `Cond_HasTheCapabilityNumber` — Current Perso has capability by bit number
- `Cond_PersoHasTheCapability` — Specified Perso has capability
- `Cond_HasOneOfTheCapabilities` — Bitmask OR check
- `Proc_CapsGetCapabilities` — Read full capability mask
- `Proc_CapabilityAtBitNumber` — Set/clear individual capability bit

This system likely governs time-travel progression mechanics — acquiring new abilities across different time periods.

---

## Collision System (COL_)

### Zone Types: ZDD, ZDE, ZDM

The collision system uses three distinct zone types with different purposes:

| Zone | Full Name (French) | Purpose |
|------|-------------------|---------|
| **ZDD** | Zone de Détection Distante | Distant/trigger detection — inter-actor proximity |
| **ZDE** | Zone de Détection d'Événements | Event detection — hit/damage zones for combat |
| **ZDM** | Zone de Déplacement Mécanique | Mechanical displacement — physics collision |

**ZDD** (Distant Detection): Used for proximity triggers. Scripts check collisions between Perso ZDDs or Module ZDDs with other Persos. Supports per-type filtering.

**ZDE** (Event Detection): Used for hit detection in combat. Supports Perso-to-Perso, Module-to-Module, and Type-based matching. This is what detects sword strikes, projectile impacts, etc.

**ZDM** (Mechanical Displacement): The actual physics collision. Checks against ground, walls, slopes, ceiling, attic (low ceiling), and obstacles. Drives the dynamics response.

### Collision Geometry

The geometry-level collision supports:
- **Spheres** ↔ FaceMapDescriptors
- **AlignedBoxes** ↔ FaceMapDescriptors
- **FaceMapDescriptors** ↔ FaceMapDescriptors (mesh vs mesh)
- **Points** ↔ FaceMapDescriptors
- **Dynamic** vs **Static** element collisions

Functions like `COL_fn_vCollideDynamicElementSpheresWithStaticElementFaceMapDescriptors` reveal the full combinatorial matrix of collision checks.

### Sector Interactions

Sectors have four interaction lists per pair:
- `SECT_fn_hCreateElementLstActivityInteraction` — Activation triggers
- `SECT_fn_hCreateElementLstCollisionInteraction` — Collision rules
- `SECT_fn_hCreateElementLstGraphicInteraction` — Visual occlusion
- `SECT_fn_hCreateElementLstSoundInteraction` — Audio propagation

---

## Dynamics & Physics Engine (DNM_)

### Mechanical Material Characteristics

Each material defines physical properties:

| Property | Get/Set Functions |
|----------|------------------|
| Adhesion | `DNM_fn_xMatCharacteristicsGet/SetAdhesionCoef` |
| Absorption | `DNM_fn_xMatCharacteristicsGet/SetAbsorptionCoef` |
| Friction | `DNM_fn_xMatCharacteristicsGet/SetFrictionCoef` |
| Slide | `DNM_fn_xMatCharacteristicsGet/SetSlideCoef` |
| Progression | `DNM_fn_xMatCharacteristicsGet/SetProgressionCoef` |
| PenetrationSpeed | `DNM_fn_xMatCharacteristicsGet/SetPenetrationSpeed` |
| PenetrationMax | `DNM_fn_xMatCharacteristicsGet/SetPenetrationMax` |

These are organized in a **Link Table** with key-based lookup (`DNM_fn_ulLink_SearchKeyInLinkTableOfMecMatCharacteristics`).

### ID Cards & Environments

- **MecIdCard**: Per-actor physics profile (mass, gravity, air friction, etc.)
- **MecEnvironment**: Environmental physics zones (underwater friction, wind, etc.)
- Both have their own Link Tables for data-driven configuration

---

## Graphics Layer (GLI_) & Rendering Pipeline

Key subsystems:
- **Texture Management**: `GLI_ulGetMaximumTextureSizeForLevels`, `GLI_vSetMaximumTextureSizeForLevels`, `GLI_fGetTextureSwappingLevelRatio`
- **Waterplane**: On/Off control, refraction enable/disable, refraction parameter setting
- **Fog**: State backup/restore (`GLI_fnvSetFogStateWithBackup`)
- **Lighting**: Get/Set light states
- **Cycling Textures**: `GLI_vRefreshAllCyclingTextures` — animated texture frames
- **End of Turn**: `GLI_vEndOfTurnEngine` — frame finalization
- **Quit Detection**: `GLI_bQuitGameRequested`, `GLI_bGetQuittingReason`

### Game Material (GMT_)

Materials are dual-purpose:

- **Mechanical properties**: Friction, adhesion, slide coefficients (affect physics)
- **Visual properties**: Color, specular, diffuse, ambient, texture scrolling, animated frame count
- Separate init paths for 3DOS and A3D (two 3D renderers supported)

---

## Camera System (CAM_)

- **Structure Init**: `CAM_fn_vInitCameraStructure`
- **Battle Camera**: `CAM_fn_vInitLevelBattleData`, `CAM_fn_vGameInitBattleData`, `CAM_fn_vGetNextTargettableCharacter`
- **Update Patch**: `CAM_fn_cActivateSectorUpdatePatch`
- **Per-Frame Override**: `CAM_fn_vSetBattleCameraParametersFor1Frame`
- **Script Control**: `Proc_Camera_UpdatePosition`, `Proc_Cam_ChangeTgtChannel`, `Proc_Cam_StopTargettingChannel`
- **Orientation Queries**: `Cond_Camera_IsCamInAlphaOrientation`, `Cond_Camera_IsCamInTetaOrientation`

The battle camera system has dedicated data per-level and per-game, with targeting logic that cycles through eligible characters.

---

## Waypoint & Navigation (WP_ / WPG_)

A comprehensive navigation system:

- **Waypoint Loading**: `WP_fne_WayPoint_ScriptCallBackLoad` — loads individual waypoints
- **Ways**: `WP_fne_Way_ScriptCallBackLoad` — loads path segments
- **Graphs**: `WP_fne_WPGraph_ScriptCallBackLoad` — loads navigation graphs
- **Sommets** (Vertices): `WP_fne_WPSommet_ScriptCallBackLoad` — graph nodes
- **Links**: `WP_fnv_Link_BuildFromScriptParams`, `WP_fnv_Link_UpdateDynamicInfo`
- **Dynamic Connections**: Lines that can be activated/deactivated at runtime

The scripting API exposes a full graph library:
- **Network Functions**: `Func_ReseauCheminLePlusCourt` (shortest path!), `Func_ReseauLitIndexCourant`, `Func_ReseauIncrementIndex`, `Func_ReseauDecrementIndex`
- **Waypoint Queries**: `Func_CloserWP`, `Func_DistanceToWP`, `Func_DistanceCaracToWP`

---

## Dialog System (DLG_)

- **Script Callbacks**: `DLG_vRegisterAllScriptCallback`, `DLG_vDeRegisterAllScriptCallback`
- **Init**: `DLG_fn_vInitStencils`, `DLG_fn_vInitMemory`, `DLG_fn_vInitDraw`
- **Timer**: `DLG_fn_TimerEnabled` — Enables a dialog timer in the game state
- **Save/Load**: `DLG_fn_vSNALoadInLevel`, `DLG_fn_vSNAWriteInLevel`
- **Script Queries**: `Cond_DLG_IsDialogOver`, `Cond_DLG_IsScrollingOver`, `Cond_GetDialogStatus`

The stencil initialization suggests dialog bubbles or text boxes use stencil buffer rendering. The `SNA` (snapshot) functions indicate dialog state is included in save data.

---

## Sound System (SND_)

- **Init**: `SND_fn_vInitMallocSnd`, `SND_fn_vInitErrorSnd`, `SND_fn_vInitBankSnd`, `SND_fn_vInitThreadSnd`
- **Threaded**: Sound runs on its own thread with dedicated initialization
- **Script Control**: `Proc_SendSoundRequest`, `Proc_SendVoiceRequest`, `Proc_SendMusicRequest`
- **Speech**: `Proc_LSY_StartSpeech`, `Proc_LSY_StopSpeech`, `Cond_LSY_IsSpeechOver`
- **Song Completion**: `Cond_SND_IsSonFinished`
- **Animation Events**: `AnimEff.c` and `MSSound.c` handle per-animation sound triggers

---

## Input System (IPT_)

- **Init**: `IPT_fn_vInitInput`, `IPT_fn_vInitCommandLines`, `IPT_fn_vInitCommandLinesSwapper`
- **Reading**: `IPT_fn_vReadInput`
- **Joystick/Pad**: `IPT_fn_bIsJoystickControlAvailable`, `IPT_fn_vActiveJoystickControl`, `IPT_fn_vActivePaddleControl`, `IPT_fn_vDesactiveJoystickAndPadControl`
- **Frame Counter**: `IPT_fn_lHowManyFrame`, `IPT_fn_ulHowManyAbsoluteFrame`
- **Button State**: `IPT_fn_ResetButtonState`
- **Entry Actions**: `IPT_fn_hGetEntryActionHandle`, `IPT_fn_hGetEntryActionHandleForOptions`, `IPT_fn_ulNumberOfEntryAction`
- **Command Line**: `IPT_fn_szCommandLineAction`

Script Conditions for input: `Cond_PressedBut`, `Cond_JustPressedBut`, `Cond_ReleasedBut`, `Cond_JustReleasedBut`

---

## Menu System (MNU_)

- **Init**: `MNU_fn_vInitMenus`, `MNU_fn_vInitLoad`, `MNU_fn_vInitMenuByHandle`, `MNU_fn_vInitSpecialItem`, `MNU_fn_vInitSliders`, `MNU_fn_vInitLevelMemory`
- **Engine**: `MNU_fn_vInternalEngine` — Timer-based menu update system
- **Active Menu**: `MNU_fn_xGetActiveMenu`, `MNU_fn_vActiveMenuByHandle`
- **Script Control**: `Proc_StartMenuWithPauseGame`, `Proc_StartMenuWithoutPauseGame`

The internal engine uses a timer-based callback system where menu items have durations, callback functions, and chain-linked next menus.

---

## Sector System (SECT_ / SCT_)

Sectors partition the game world for:
- **Activation Flags**: `SECT_fn_vInitSectorActivationFlag`
- **Sound Events**: `SCT_fn_ucGetGameStateInSoundEventList`
- **GoThrough Materials**: `SCT_fn_vInitMaterialForGoThrough`
- **Sector Rendering**: `SCT_fn_vSendSectorWhereIAmToViewportWithMirror`
- **Script Manipulation**: `Proc_RotateSector*`, `Proc_TranslateSector`, `Cond_SectorActive`, `Cond_IsSectorInTranslation/Rotation`

---

## Additional Subsystems

| Prefix | Name | Purpose |
|--------|------|---------|
| **POS_** | Position | Matrix operations, identity, translation vectors, dynamic vectors |
| **PRT_** | Particles | Particle system with generator modes (Continuous, Crenel, Probability) |
| **RND_** | Random | Random number generation with script integration |
| **SHW_** | Shadows | Dynamic shadow system with per-frame counter |
| **FON_** | Fonts | Text rendering with extensive effects (translation, rotation, scale, wave, light) per-character |
| **DPT_** | Data Paths | File path management for game data and engine DLLs |
| **ENV_** | Environment | Environment loading and configuration |
| **Mmg_** | Memory Manager | Block-based memory allocation with module tracking |
| **Erm_** | Error Manager | Error reporting with module/mode context, message boxes |
| **SCR_** | Script File | `.scr` file parsing and update notification system |
| **TEX_** | Textures | Binary texture file management |

---

## Compatibility Issues & DRM Protection

The game ships with a simple in-house copy protection system and several hard-coded requirements that cause problems on modern operating systems. **No third-party DRM** (SafeDisc, SecuROM, Denuvo, etc.) is present.

### CD-ROM Disc Check (DRM)

The protection is implemented via two functions that share the same core logic:

| Function | Address | Called From | Trigger |
|----------|---------|-------------|---------|
| `sub_4081D0` | `0x4081D0` | `sub_402180` (early init) | **Game startup** |
| `sub_4046A0` | `0x4046A0` | `sub_4089D0` (video player) | **Every video/cutscene playback** |

#### How the Check Works

Both functions delegate to the **CD scanner** `sub_408CA0` (`0x408CA0`), which performs:

```
sub_408CA0(lpRootPathName):
  1. GetLogicalDriveStringsA()     → enumerate all drive letters
  2. For each drive:
     a. GetDriveTypeA(drive)       → check if type == 5 (DRIVE_CDROM)
     b. If CDROM:
        GetVolumeInformationA()    → read the disc's volume label
        stricmp(label, "Hype")     → case-insensitive comparison
        If match → return drive letter
  3. If no match found → return 0 (null drive letter)
```

When the scanner returns no drive letter (result is `0` / null), the calling function:
1. Checks the **"Complete" flag** (`dword_5F3CC8`) — if non-zero, **skips** the error
2. Otherwise, shows a **localized error message** and calls `exit(-1)`:
   - **English**: `"Insert the Original CD in the CD-ROM drive and restart the game"`
   - **French**: `"Ins..."` (truncated in binary — likely "Insérez le CD original")
   - **German**: `"Lege die Original-CD in das CD-ROM Laufwerk..."` (truncated)

The message is displayed via `Erm_fn_iMessageBox` with the title `"CD-ROM"` and `MB_ICONINFORMATION` flag (`0x40000`).

#### Built-in Bypass: The `-cdrom:` Command-Line Argument

Before scanning drives, both functions check for a **`-cdrom:`** command-line parameter:

```c
if (strstr(GetCommandLine(), "-cdrom:")) {
    // Extract the drive letter after "-cdrom:"
    strncpy(&driveLetter, strstr(cmdLine, "-cdrom:") + 7, 1);
    sprintf(path, "%c:\\Gamedata\\Videos", driveLetter);
    // Skip the CD scanner entirely
}
```

This allows overriding the drive detection by specifying a letter directly, e.g.:
```
Hype.exe -cdrom:D
```

#### Built-in Bypass: The "Complete" INI Flag

The `Ubi.ini` file contains a **`Complete`** key under the game's section. When `Complete=1`, the CD check **still runs** (the scanner still executes), but if it fails, the error message and `exit(-1)` are **skipped**. The game continues with a fallback drive letter of `'c'` (`0x63`).

The critical assembly at `0x40482B`–`0x40483B`:
```asm
; After CD scan returned null drive letter
40482B  test    al, al               ; driveLetter == 0 ?
40482D  jnz     short loc_4048A3     ; if found → skip error, continue
40482F  mov     eax, dword_5F3CC8    ; eax = "Complete" flag from Ubi.ini
404834  mov     Destination, 63h     ; fallback drive = 'c'
40483B  test    eax, eax             ; Complete flag != 0 ?
40483D  jnz     short loc_4048A3     ; if Complete → skip error, continue
; ... otherwise show error and exit(-1)
```

### Ubi.ini Configuration System

The game reads its configuration from `%WINDIR%\UbiSoft\Ubi.ini` (typically `C:\Windows\UbiSoft\Ubi.ini`) using **`GetPrivateProfileStringA`**. The config reader is at `sub_408480` (`0x408480`).

| INI Key | Default | Purpose |
|---------|---------|-------|
| `DDDriver` | — | DirectDraw driver index. If `"Not Selected Yet"`, spawns `UbiAssistant.exe` |
| `D3DDevice` | `"2"` | Direct3D device index |
| `TexturesMem` | (from ptr) | Texture memory mode |
| `ParticuleRate` | `"Medium"` | Particle density: `Null`, `Low`, `Medium`, `High` |
| `Language` | `"English"` | Display language |
| `SoundOnHD` | — | Sound files on hard disk (vs. CD streaming) |
| `SoundStream` | — | Enable sound streaming |
| **`Complete`** | `"1"` | **Full installation flag — bypasses CD check on failure** |
| `PlayVideo` | `"1"` | Enable/disable video cutscenes |
| `UseTripleBuffer` | — | Triple buffering toggle |
| `LowGraphicMode` | — | Enables degraded graphics via `GLI_vEnableLowGraphicMode` |
| `MipMapping` | `"Auto"` | Mipmap mode: `On`, `Off`, `Auto` |
| `Trilinear` | `"Off"` | Trilinear filtering: `On`, `Off` |
| `ForceVideoMode` | `"Default"` | Video playback mode: `Default`, `DXMedia`, `ChildWindow` |

> [!IMPORTANT]
> If `DDDriver` is set to `"Not Selected Yet"`, the game attempts to `spawnl` the **UbiAssistant.exe** setup tool. If that tool is missing (it usually is on modern systems), the game calls `exit(-1)` immediately. This is a common cause of instant crashes on fresh installations.

The resolution is **hard-coded to 640×480** via `sub_402460(640, 480)` — there is no INI key to change it.

### DirectX & Display Compatibility

The game targets **DirectX 5/6 era APIs**, creating serious compatibility issues on modern systems:

#### 16-bit Color Depth Requirement

The game enforces a strict **16-bit (32768 colors) display mode**:
```
"The game must be run with a display settings of 16 bits per pixels. (32768 colors)"
```
This check is performed at startup. Modern displays default to 32-bit color, causing the game to refuse to launch.

#### DirectDraw / Direct3D Legacy

The game imports from:
- **`DDRAW.dll`** — `DirectDrawCreate`, `DirectDrawEnumerateA`
- **`DINPUT.dll`** — `DirectInputCreateA`
- **`WINMM.dll`** — `timeGetTime`, `timeBeginPeriod`

D3D hardware detection (`sub_46EA30`) iterates through available D3D devices and shows:
```
"No D3D hardware driver found. Please reinstall DirectX."
```
if no suitable device is found. On modern systems with only Direct3D 9+ drivers, the legacy D3D5/6 device enumeration may fail entirely.

Additional DirectDraw error strings found in the binary:
- `"SetDisplayMode failed"` — fullscreen mode switch failure
- `"SetCooperativeLevel failed"` — exclusive mode acquisition failure  
- `"CreateDevice : Can't get DirectDraw2 Interface"` — DDraw2 unavailable
- `"SwapDeviceMode : Another device is already in fullscreen mode"` — exclusive mode conflict
- `"LoadPictures : Can't create DirectDraw object for pictures"` — texture loading failure
- `"DIERR_OLDDIRECTINPUTVERSION"` — DirectInput version mismatch

#### BMP Vignette Format Lock

Loading screen images must be exactly **640×480, 24-bit, uncompressed BMP**:
```
"fn_vLoadBMPVignette : Bad file format (must be 640*480, 24 bits, not compressed)."
```

### Patching Guide

The following patches address the major compatibility blockers:

#### Patch 1: Disable CD Check (sub_4046A0)

Make the function always return immediately with a valid drive letter, bypassing the entire CD scan.

| Offset (file) | Original | Patched | Effect |
|---------------|----------|---------|--------|
| `0x4046A0` | `81 EC 04 01 00 00` | `B0 43 C3 90 90 90` | `mov al, 'C'` / `ret` / `nop×3` — function returns immediately with drive letter 'C' |

Alternatively, patch just the conditional jump to always skip the error:

| Address | Original | Patched | Effect |
|---------|----------|---------|--------|
| `0x40482B` | `75 76` (`jnz +0x76`) | `EB 76` (`jmp +0x76`) | Unconditional jump — always skip the CD error check |

#### Patch 2: Disable CD Check (sub_4081D0)

Same approach for the startup check:

| Address | Original | Patched | Effect |
|---------|----------|---------|--------|
| `0x4082E5` | `84 C0` (`test al, al`) | `B0 01` (`mov al, 1`) | Force drive letter to be non-zero — skip error path |

#### Patch 3: INI-Based Bypass (No Binary Modification)

The **safest and cleanest** approach requires no binary patching at all:

1. Locate or create `%WINDIR%\UbiSoft\Ubi.ini`
2. Under the game's section (typically `[Hype The Time Quest]`), set:
```ini
[Hype The Time Quest]
Complete=1
SoundOnHD=1
SoundStream=1
PlayVideo=1
DDDriver=0
D3DDevice=0
```

The `Complete=1` flag is read into `dword_5F3CC8`. When the CD check fails, this flag causes the game to **skip the error message and `exit(-1)` call**, falling through with a default drive letter of `'c'`.

> [!CAUTION]
> With `Complete=1`, the game still tries to access `C:\Gamedata\Videos\` for cutscene files. If game data is installed elsewhere, cutscenes will fail silently. Use the `-cdrom:` command-line argument to redirect to the correct drive, or ensure all game data is accessible from the installation directory.

#### Patch 4: Fix UbiAssistant.exe Crash

If `DDDriver` is `"Not Selected Yet"` (or the key is missing), the game tries to run `UbiAssistant.exe` and exits if it fails. To prevent this:

1. Set `DDDriver=0` in `Ubi.ini` to select the first available DirectDraw driver
2. Set `D3DDevice=0` to select the first available Direct3D device

#### Patch 5: 16-bit Color Depth Workaround

The 16-bit color depth check (string at `0x5DE4FC`) can be bypassed in two ways:

1. **Windows Compatibility Mode**: Right-click `Hype.exe` → Properties → Compatibility → Check "Reduced color mode" → Select "16-bit (65536) color"
2. **dgVoodoo2**: Use the [dgVoodoo2](http://dege.freeweb.hu) DirectX wrapper, which intercepts legacy DirectDraw/Direct3D calls and translates them to modern Direct3D 11/12. This resolves the color depth, resolution, fullscreen, and device enumeration issues simultaneously.

#### Recommended Modern Setup

For the best experience on modern Windows (10/11):

1. Install dgVoodoo2 (copy `DDraw.dll`, `D3DImm.dll` to the game directory)
2. Set `Ubi.ini` with `Complete=1`, `DDDriver=0`, `D3DDevice=0`
3. Right-click `Hype.exe` → Compatibility → Run as Administrator, Windows XP SP3 mode
4. Use `-cdrom:C` command-line argument if cutscenes fail
5. Optionally set `PlayVideo=0` to skip cutscenes entirely if video playback causes crashes

---

## Particularities & Observations

### 1. French-English Mixed Naming
The codebase mixes French and English naming, revealing its Montpellier origins:
- `Func_LitPointsDeMagie` ("Read Magic Points")
- `Func_AjouteEtLitPointsDeMagie` ("Add And Read Magic Points")
- `Func_PersoLePlusProche` ("Nearest Character")
- `Func_VitesseHorizontaleDuPerso` ("Horizontal Speed Of Character")
- `Func_ReseauCheminLePlusCourt` ("Network Shortest Path")
- `Func_CalculVecteurRebond` ("Calculate Bounce Vector")
- `Func_LitDernierPersoCollisione` ("Read Last Collided Character")

### 2. .CAR / .DEC File Validation
Error messages reveal that DsgVar definitions live in `.CAR` (character) files and `.DEC` (declaration?) files, with runtime validation:
- `"Difference in DsgVar between .CAR and .DEC [Unknown DsgVar name (%s) No %s ignored]"`
- `"This perso has no dsgvar anymore, so corresponding .CAR is an old one"`

### 3. SNA (Snapshot) Persistence
The save system uses "SNA" snapshots at multiple levels:
- Per-level dialog state (`DLG_fn_vSNALoadInLevel`)
- Sector rotations (`Proc_LevelSaveRotationSector`, `Proc_PlayerSaveRotationSector`)
- Moving surfaces (`Proc_LevelSaveMovingSurface`, `Proc_PlayerSaveMovingSurface`)
- Full game values (`Proc_SaveAllGameValues`)
- Historic progression (`Proc_IncHistoricAndSaveGame`)

### 4. Dual-Renderer Architecture
The `GMT_fn_vInitFor3DOS` and `GMT_fn_vInitForA3D` functions suggest the engine was designed to support multiple 3D rendering backends — likely software (3DOS) and hardware-accelerated (A3D/Direct3D).

### 5. Dynamic LOD System
Level-of-detail is runtime configurable per-actor:
- `Proc_AllowDynamLOD` / `Proc_ForbidDynamLOD`
- `VS_fn_eScriptCallBackLOD` — LOD-specific visual set callback

### 6. "UserEvent" System
`Cond_UserEvent_IsSet` suggests a generic event/flag system beyond the standard DsgVar mechanism — possibly for global game progression triggers.

### 7. "GiBlock" Checks
`Cond_GiBlock` appears to be a GI (GameInfo?) blocking check, potentially related to character stun/stagger mechanics.

### 8. Floor Game Mini-Game
`Func_ExecuteFloorGame` suggests embedded mini-game logic (possibly a puzzle or floor-tile game), with `Func_GPI_DifferentFromCurrentImage` and `Func_GPI_IsDrawn` indicating a tile/image-based puzzle mechanic.

### 9. Laser Pointer System
`Proc_GetLaserPointerDirection/Distance/DistanceWithMap` indicates an aiming or cursor-based targeting system, possibly used for magic casting or ranged attacks.

### 10. Table/Array System
`Func_TBL_InitArray` combined with `Proc_ChangeCurrentObjectTable`, `Proc_CopyObjectFromTableToTable`, and `Proc_SwapLinkTableObjects` reveal a generic array/table system used for managing collections of game objects.

---

*Analysis performed via IDA Pro reverse engineering of `Hype.exe`. All function names, string references, and decompiled pseudocode are from the original binary. No source code was available.*
