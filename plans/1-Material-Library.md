# Material Library

## Purpose
Create a material library so parts can be assigned a material. A material stores
material name/type, thickness, and density. Assigning a material to a part
allows the system to display part thickness and calculate part weight when
geometry data is available.

This also supports future nesting workflows where selected parts can be grouped
into separate nests by material.

## Current Scope
- Create a material library UI for viewing and managing materials
- Allow users to add and edit materials
- Store materials in the postgres database
- Allow users to assign a material to a part from:
  - `MCBatchAddPartNames`
  - `MCAddPartMetadata`
- Persist the selected material on the part record
- Display part thickness and weight when possible

## Future Scope
- Group nests by material
- Material-based reporting
- Import/export materials

## Technical Context
- Project type: C# AutoCAD extension
- UI framework: [WPF or WinForms]
- Database: [SQL Server / SQLite / other]
- Migration approach: [raw SQL / EF Core / other]
- Follow existing project architecture and patterns
- Do not break existing command behavior

## Data Model
A material should contain:
- `Id`
- `Name`
- `ThicknessMm`
- `DensityKgPerMm3`
- `Description` optional
- `IsActive`
- `CreatedAt`
- `UpdatedAt`

A part should reference a material by `MaterialId`.

## Business Rules
- `Name` is required
- `ThicknessMm` is required and must be greater than 0
- `DensityKgPerMm3` is required and must be greater than 0
- Prevent duplicate active materials with the same `Name + ThicknessMm`
- Materials already assigned to parts should not be hard deleted
- Inactive materials should be hidden from selection lists by default

## UI Requirements
Create a material library window that displays materials in a table/grid.

Columns:
- Name
- Thickness (mm)
- Density
- Description
- Active

Actions:
- Add Material
- Edit Material
- Refresh list
- Search/filter by material name

Add Material form fields:
- Name
- Thickness
- Density
- Description
- Active

Validation errors should be shown to the user before save.

## Database Requirements
- Add a `Materials` table
- Add `MaterialId` foreign key reference on the part metadata table
- Create a SQL migration file for schema changes
- Follow existing naming conventions in the project

## Command Integration
Update these commands to allow selecting a material from the material library:
- `MCBatchAddPartNames`
- `MCAddPartMetadata`

Requirements:
- User can choose an existing material
- Selected material is saved with the part metadata
- Existing metadata should be updated, not duplicated

## Weight Calculation
- Thickness is stored in millimeters
- Density is stored in `kg/mm^3`
- Part weight should be calculated from part geometry volume × density
- If geometry data is insufficient to calculate weight, still display thickness
  and leave weight blank

## Acceptance Criteria
- User can open the material library window
- User can add a material successfully
- User can edit an existing material successfully
- Materials persist in the database
- User can assign a material in `MCBatchAddPartNames`
- User can assign a material in `MCAddPartMetadata`
- Assigned material is stored on the part record
- Part thickness is displayed from the selected material
- Part weight is displayed when geometry supports calculation
