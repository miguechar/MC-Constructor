# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

**MCConstructor** is a C# ObjectARX extension for AutoCAD 2024 that provides manufacturing design automation: project management, part metadata tracking, material libraries, plate nesting optimization, and structural profile support. It runs inside AutoCAD as a loaded DLL.

- **Language/Runtime**: C# 7.3 / .NET Framework 4.7.2
- **Host Application**: AutoCAD 2024 (via ObjectARX .NET API — AcCoreMgd, AcDbMgd, AcMgd, AdWindows)
- **Database**: PostgreSQL via Npgsql 4.1.13 (raw parameterized SQL, no ORM)
- **UI**: WPF dialogs + AutoCAD Ribbon + WinForms PaletteSet

## Build

Open `MCConstructor.sln` in Visual Studio 2022 and build normally, or:

```bash
msbuild MCConstructor.sln /p:Configuration=Release /p:Platform="Any CPU"
```

Output: `MCConstructor\bin\Release\MCConstructor.dll`

ObjectARX 2024 SDK must be installed. Reference paths in `.csproj` expect the ObjectARX DLLs at their default install location.

## Loading & Running the Plugin

There is no standalone test runner. All testing is manual inside AutoCAD:

1. Type `NETLOAD` in AutoCAD → select the built DLL
2. The "MC Constructor" ribbon tab appears on idle
3. Run `MCConfigDatabase` to set the PostgreSQL connection string (not persisted between sessions)
4. Use ribbon buttons or type commands directly (e.g. `MCCreateProject`, `MCCreateNest`)

For debugging, set Visual Studio's start action to the AutoCAD executable, build Debug, then NETLOAD the debug DLL — breakpoints work normally.

## Database Setup

Run SQL migrations once against a PostgreSQL database:

```bash
psql -U postgres -d <dbname> -f MCConstructor/SQL/create_projects_table.sql
psql -U postgres -d <dbname> -f MCConstructor/SQL/create_drawings_table.sql
psql -U postgres -d <dbname> -f MCConstructor/SQL/create_drawing_parts_table.sql
psql -U postgres -d <dbname> -f MCConstructor/SQL/create_materials_and_profiles.sql
```

## Architecture

### Entry Point & Ribbon (`MyPlugin.cs`)

Implements `IExtensionApplication`. Defers ribbon creation to AutoCAD's idle event (required for safe initialization). Registers all ribbon button click handlers and loads/generates icons. All icons are loaded from `<DLL dir>\Images\<CommandName>.png`; if missing, a category-colored square is generated at runtime.

### Commands (`Commands.cs`)

All 20+ AutoCAD commands are `[CommandMethod]` handlers on a single class. This is the main integration surface with AutoCAD. Commands launch WPF dialogs, call services, and manipulate the AutoCAD document.

### Services (static classes)

| Service | Responsibility |
|---|---|
| `DatabaseService` | All CRUD for projects, drawings, and parts (3D geometry stored as SAT text + Wblock'd bytes) |
| `MaterialLibraryService` | CRUD for materials, plate stock sizes, and structural profiles — reuses `DatabaseService`'s connection string |
| `NestingService` | MAXRECTS bin-packing layout engine; places parts onto plates, returns placed/unplaced lists and efficiency % |

Services are static with no DI container. `DatabaseService.SetConnectionString()` must be called before any DB operation (done via `MCConfigDatabase`).

### Data Model

All entities are project-scoped (foreign keyed to `projects`). Key tables:

- `drawing_parts` — the core part table: stores part name, SAT geometry, bounding box, layer/color/material, and reference tracking (`is_original`, `is_override`, `parent_part_id`)
- `materials` + `material_plates` — per-project material library with stock plate sizes
- `profiles` — structural sections with optional density override and cached cross-section area

Parts carry both a DB record and AutoCAD XData (app name `"MC_CONSTRUCTOR"`) that survives save/load. The DB record and the entity are linked by `source_drawing_name + source_object_handle`.

### Nesting Flow

`MCCreateNest` → `NestingDialog` (select material/plate/spacing) → `NestingService.NestParts()` → creates a new drawing from the project's nest template, inserts Wblock'd part geometry at computed positions, sets layer colors, and annotates with part labels.

### UI Layer

- **WPF Dialogs**: All user-input dialogs inherit from `Window`, are modal, and expose result properties after `ShowDialog()`.
- **`MetadataPalette`**: Dockable WinForms `PaletteSet` that shows XData metadata for the currently selected entity; subscribes to document events to stay in sync.

## Key Conventions

- **Namespace**: Everything in `MCConstructor` — no sub-namespaces.
- **AutoCAD alias**: `using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application` (used throughout).
- **SQL**: Always parameterized — never build SQL strings by concatenation.
- **New dialogs**: Prefer WPF (`Window`) over WinForms.
- **New DB tables**: Add a new `.sql` file under `SQL/`, then add a `<Content ... CopyToOutputDirectory>` entry in `.csproj` so it gets copied alongside the DLL.
- **New commands**: Add `[CommandMethod("MC...")]` in `Commands.cs` and a ribbon button in `MyPlugin.CreateRibbon()`.

## Active Work

- **Material Library UI** (`plans/1-Material-Library.md`): Data model is complete; the `MaterialLibraryWindow` WPF dialog and material-picker integration in `MCAddPartMetadata`/`MCBatchAddPartNames` are not yet built.
- **Profile Plotting** (`plans/2-Profile-Plots.md`): Planned feature to generate process sheets for structural profiles — not started.
