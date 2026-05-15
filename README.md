# MC Constructor — AutoCAD Plugin

MC Constructor is a C# ObjectARX plugin for AutoCAD 2024 that provides manufacturing design automation: project management, drawing registration, part metadata, material libraries, plate nesting, and structural profile support.

---

## Requirements

- AutoCAD 2024 (64-bit)
- .NET Framework 4.7.2
- PostgreSQL database (any recent version)
- ObjectARX 2024 SDK (for building from source)

---

## Installation

1. Build the solution in Visual Studio 2022 (`Release` configuration).
2. In AutoCAD, type `NETLOAD` and select `MCConstructor.dll`.
3. The **MC Constructor** ribbon tab appears automatically.
4. Run `MCConfigDatabase` to connect to your PostgreSQL database.

---

## First-Time Database Setup

Run these SQL migrations once against your PostgreSQL database, in order:

```bash
psql -U postgres -d <dbname> -f SQL/create_projects_table.sql
psql -U postgres -d <dbname> -f SQL/create_drawings_table.sql
psql -U postgres -d <dbname> -f SQL/create_drawing_parts_table.sql
psql -U postgres -d <dbname> -f SQL/create_materials_and_profiles.sql
psql -U postgres -d <dbname> -f SQL/add_material_id_to_drawing_parts.sql
psql -U postgres -d <dbname> -f SQL/add_profile_drawing_types.sql
```

---

## Project Folder Structure

Every project gets a standard folder layout created automatically under the chosen root directory:

```
<ProjectRoot>/
  00 Admin/
  01 Standards/
    Templates/          ← Put NEST TEMPLATE.dwg and PROFILE_PLOTS_TEMPLATE.dwg here
    Titleblocks/
    Blocks/
    Profiles/           ← Cross-section .dwg files for structural profiles
  02 Models/
    General/
    Piping/
    Electrical/
    HVAC/
  03 Sheets/            ← Nest output drawings
  03 Fabrication/
    Profile Plots/      ← Generated profile plot sheets
  04 Output/
```

---

## Commands Reference

All commands are accessible from the **MC Constructor** ribbon tab or by typing the command name directly in the AutoCAD command line.

---

### Setup

#### `MCConfigDatabase`
Configure the PostgreSQL connection string.

- Opens a dialog where you enter the connection string (e.g. `Host=localhost;Database=mc;Username=postgres;Password=...`).
- The connection string is stored in memory for the current AutoCAD session only — it must be re-entered after each restart.
- **Run this first** before any other command that touches the database.

---

### Project Management

#### `MCCreateProject`
Create a new project and build its folder structure on disk.

1. Enter project name and description.
2. Choose a parent directory on disk.
3. The plugin creates the standard folder layout under `<parent>/<ProjectName>/` and registers the project in the database.
4. The new project is automatically set as the active project.

**Requires:** Database configured (`MCConfigDatabase`).

---

#### `MCOpenProject`
Open (activate) an existing project from the database.

- Opens a picker showing all projects in the database.
- The selected project becomes the active project for the current session.

---

#### `MCProjectStatus`
Display a status summary in the AutoCAD command line.

Shows:
- Active project name
- Number of parts saved to the database
- Current drawing filename
- Number of 3D solids in the drawing
- How many solids have metadata

---

### Drawings

#### `MCCreateDrawing`
Create a new `.dwg` file registered to the active project.

1. Choose drawing name, type, discipline, description, and an optional template.
2. The file is saved into the appropriate project subfolder based on type.
3. The drawing is registered in the database and opened in AutoCAD.

**Drawing Types:** Functional Drawing, Detail Drawing, Block Library, Template, Titleblock, Sheet, Profile (Cross-Section), Other.

**Requires:** Active project.

---

#### `MCNavigator`
Open the project navigator window.

- Lists all drawings registered for the active project, grouped by type:
  - Functional Drawings (sub-grouped by discipline)
  - Detail Drawings
  - Block Library
  - Templates
  - Titleblocks
  - Sheets
  - **Profile Drawings**
  - **Profile Plots**
  - Other
- Shows a thumbnail preview and file metadata when a drawing is selected.
- Double-click or click **Open** to open the drawing in AutoCAD.
- Only shows drawings whose `.dwg` file exists on disk under the project directory.

**Requires:** Active project.

---

#### `MCDrawingProperties`
View and edit the MC drawing properties for the currently open drawing.

- Shows drawing type, discipline, project name, and description stored in the file's summary info.
- Changes are saved back into the `.dwg` file.

---

### Parts & Metadata

#### `MCAddPartMetadata`
Add a part name and material to a single 3D solid.

1. Select a 3D solid.
2. Enter a part name and optionally select a material from the project library.
3. The data is stored as XData on the entity and survives save/reload.

---

#### `MCBatchAddPartNames`
Apply sequential part names to a multi-object selection in one operation.

1. Select any number of entities (3D solids, polylines, regions, etc.).
2. Enter a name prefix and starting serial number.
3. Optionally assign a material to all selected parts.
4. Each entity is named `<prefix>-<serial>`, with the serial incrementing by one per object.

**Example:** prefix `A1LB1-00-D07`, start `100` → names `A1LB1-00-D07-100`, `A1LB1-00-D07-101`, …

---

#### `MCViewPartMetadata`
Print the XData metadata for a selected 3D solid to the command line.

- Selects a single 3D solid and lists all stored key-value pairs (part name, material, part ID, etc.).

---

#### `MCShowMetadataPalette` / Part Properties Palette
Toggle the **MC Part Properties** dockable palette.

- Automatically updates when you click a 3D solid.
- Displays: Handle, Layer, Width X, Depth Y, Height Z, Material, Profile, Part Name.
- The **Part Name** field is editable — click **Apply** to write the new name back as XData.
- The **Profile** field is set automatically when parts are inserted with `MCInsertProfile`.

---

#### `MCListParts`
List all parts with metadata in the current drawing (command line output).

---

### Database Parts

#### `MCSaveParts`
Save all named 3D solids in the current drawing to the project database.

- Scans model space for 3D solids that have a part name set via `MCAddPartMetadata` or `MCBatchAddPartNames`.
- Serializes full geometry (ACIS BREP) as a `.dwg` blob for high-fidelity recreation.
- Creates or updates the corresponding `drawing_parts` rows.

---

#### `MCInsertPart`
Insert a copy of a database part into the current drawing.

1. Pick a part from the project database list.
2. Specify an insertion point.
3. The part's exact geometry is cloned into model space at that point.
4. The inserted copy is registered as a reference (linked to the original) in the database.

---

#### `MCOverridePart`
Mark a part reference as locally overridden.

- Select a part reference (inserted via `MCInsertPart`).
- Confirm the override.
- The current geometry state is saved to the database as an override record.
- Run `MCUpdateAllParts` in the source drawing to propagate changes back to the original.

---

#### `MCUpdateAllParts`
Synchronise part references in the current drawing from the database.

- Applies any pending overrides to their originals.
- Updates all reference parts in the drawing to match the latest database record.

---

#### `MCListDbParts`
List all original parts saved to the database for the active project (command line output).

---

### Material Library

#### `MCMaterialLibrary`
Open the material library editor for the active project.

- Manage **materials** (steel grade, density).
- Manage **plate stock sizes** (width × length per material).
- Manage **structural profiles** (cross-section drawings, density, area).
- Each project has its own independent library.

**Requires:** Active project.

---

### Nesting

#### `MCCreateNest`
Create an optimised plate nesting layout.

1. Select 3D solids to nest.
2. Choose a material, plate size, part spacing, and output drawing name in the dialog.
3. The plugin projects each part to a 2D bounding footprint and runs a MAXRECTS bin-packing algorithm.
4. A new drawing is created with the parts laid out on the plate. If a `NEST TEMPLATE.dwg` exists in the project's `01 Standards\Templates\` folder, it is used as the base drawing (bringing the title block along automatically).
5. Parts that did not fit are reported in the command line.

**Output:** `<project>/03 Sheets/<drawing name>.dwg`  
**Template:** `<project>/01 Standards/Templates/NEST TEMPLATE.dwg` (optional)

**Requires:** Active project, parts selected in the current drawing.

---

#### `MCQuickNest`
Quick nesting with a default 2440 × 1220 mm plate.

- Same as `MCCreateNest` but skips the plate-size dialog and uses the default plate immediately.
- Useful for rapid iteration when the plate size is always standard.

---

### Profiles

#### `MCInsertProfile`
Extrude a structural profile cross-section and insert it into the drawing.

1. Pick a **Profile** drawing from the project library.
2. **Select lines to follow** (optional): select one or more Line entities. Each line's direction and length drive one extrusion — the solid is rotated and translated to match that exact line. Multiple lines produce one solid per line.
3. **Press Enter** (manual mode): specify an insertion point and enter the extrusion length in the dialog. The solid is placed along the Z axis at that point.
4. The profile name is automatically stored as XData (`ProfileName`) on each inserted solid and is visible in the Part Properties Palette.

**Requirements:**
- Profile cross-section drawings must be registered in the database with type **Profile** and the 2D closed curves must be at the origin (0, 0, 0) in the profile drawing.
- Active project must be open.

---

#### `MCCreateProfilePlot`
Generate a process-sheet drawing showing three views of a profile part.

1. Optionally select a 3D solid to pre-fill the length (the longest bounding-box dimension is used).
2. Choose the profile cross-section drawing, enter the length, and enter a plot name.
3. A 2000 × 1500 mm process sheet is generated with:
   - **Front Elevation** — length × section height
   - **Plan View** — length × section width
   - **End View** — true cross-section geometry (cloned from the profile drawing)
   - **True-value dimensions** on all views (the drawing is scaled to fit; dimension text always shows the real mm value)
4. The file is saved to `<project>/03 Fabrication/Profile Plots/` with a unique filename (counter suffix if the name already exists).
5. The plot is registered in the database as a **Profile Plot** drawing so it appears in `MCNavigator`.
6. The generated drawing is opened automatically in AutoCAD.

**Template:** If `PROFILE_PLOTS_TEMPLATE.dwg` exists in `<project>/01 Standards/Templates/`, it is used as the base drawing. Place your title block, border, layers, and dim styles there — they carry into every plot automatically. When a template is used, dimension styling is read from the template.

**Output:** `<project>/03 Fabrication/Profile Plots/<PlotName>.dwg`  
**Template:** `<project>/01 Standards/Templates/PROFILE_PLOTS_TEMPLATE.dwg` (optional)

**Requires:** Active project, at least one Profile drawing registered.

---

## Template Files

Two optional template drawings can be placed in the project's `01 Standards\Templates\` folder to control the appearance of generated drawings.

| Filename | Used by | What it provides |
|---|---|---|
| `NEST TEMPLATE.dwg` | `MCCreateNest`, `MCQuickNest` | Title block and layers for nest output sheets |
| `PROFILE_PLOTS_TEMPLATE.dwg` | `MCCreateProfilePlot` | Title block, border, layers, and dim styles for profile plot sheets |

Neither template is required — the commands fall back to a blank drawing if the template is not found.

---

## XData Keys

Parts carry XData under the application name `MC_CONSTRUCTOR`. The following keys are stored:

| Key | Set by | Description |
|---|---|---|
| `PartName` | `MCAddPartMetadata`, `MCBatchAddPartNames` | Human-readable part name |
| `PartId` | `MCSaveParts`, `MCInsertPart` | UUID linking the entity to its `drawing_parts` row |
| `ParentPartId` | `MCInsertPart` | UUID of the original part this is a reference of |
| `IsOriginal` | `MCSaveParts` | `True` for the source entity, `False` for inserted references |
| `MaterialId` | `MCAddPartMetadata` | UUID of the assigned material |
| `MaterialName` | `MCAddPartMetadata` | Display name of the assigned material |
| `ProfileName` | `MCInsertProfile` | Name of the profile cross-section drawing used to create this solid |

---

## Ribbon Layout

| Panel | Commands |
|---|---|
| **Project** | MCCreateProject, MCOpenProject, MCNavigator, MCCreateDrawing, MCSaveParts, MCProjectStatus |
| **Parts** | MCAddPartMetadata, MCBatchAddPartNames, MCViewPartMetadata, MCListParts |
| **References** | MCInsertPart, MCOverridePart, MCUpdateAllParts, MCListDbParts |
| **Profiles** | MCInsertProfile, MCCreateProfilePlot |
| **Nesting** | MCCreateNest, MCQuickNest |
| **Tools** | MCShowMetadataPalette, MCDrawingProperties, MCMaterialLibrary, MCConfigDatabase |

--- 

## Recursive File Unblocking
Use this PS command to recursively unblock files when downloading this repo:

```cmd
Get-ChildItem -Path "C:\path\to\your\folder" -Recurse -Force | Unblock-File
```
