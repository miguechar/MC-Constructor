# UI Style Guide — Dark Theme for AutoCAD ARX Plugin

## Purpose
Defines the preferred UI color theme and control styling for plugin dialogs and editor windows.

### Theme Overview
- **Theme Type:** Dark
- **General Style:** Flat, high-contrast text on dark surfaces, minimal borders.
- **Primary Accent Color:** Blue (#007ACC)
- **Font:** Segoe UI, 12px (default WPF font acceptable)

---

## Brush Palette

| Name | RGB | Hex | Usage |
|------|-----|-----|--------|
| `WindowBackground` | (45, 45, 48) | #2D2D30 | window main background |
| `SurfaceDark` | (37, 37, 38) | #252526 | panels and DataGrids |
| `SurfaceLight` | (50, 50, 53) | #323235 | alternating row bg |
| `Border` | (67, 67, 70) | #434346 | control lines |
| `HeaderForeground` | (160, 160, 165) | #A0A0A5 | section headers |
| `TextForeground` | (255, 255, 255) | #FFFFFF | body text |
| `ButtonNormal` | (70, 70, 75) | #46464B | standard button |
| `ButtonHover` | (0, 122, 204) | #007ACC | focus / accent |
| `ButtonDisabledBG` | (52, 52, 55) | #343437 | disabled background |
| `ButtonDisabledFG` | (100, 100, 105) | #646669 | disabled text |
| `SelectionBlue` | (0, 102, 180) | #0066B4 | row selection or OK buttons |

---

## Control Styling Rules

### TextBox
- Background: `SurfaceDark`
- Foreground: White
- Border: 1 px solid `Border`
- Padding: (6,3,6,3)

### Label
- Foreground: #C8C8C8 (≈ (200,200,200))
- Alignment: VerticalCenter

### Button
- Background: `ButtonNormal`
- Foreground: White
- BorderThickness: 0
- Padding: (12,0,12,0)
- Height: 28
- Disabled state uses `ButtonDisabledBG` and `ButtonDisabledFG`
- Default or primary button uses `SelectionBlue` / white text

### DataGrid
- Background: `SurfaceDark`
- Row background: `WindowBackground`
- Alt row: `SurfaceLight`
- Grid lines: `Border`
- Column headers: slightly lighter than rows (`#37373C`)
- Selection highlight: `SelectionBlue`

---

## General Layout
- Margin / Padding scale: 4–12 px typical
- Section headers: small caps or bold 10–11 pt gray text
- StackPanel spacing between buttons: 6 px

---

## Usage Guidance for Generation
- Always apply these colors when creating or modifying any WPF window, dialog, or control.
- Prefer programmatic brush definitions over external XAML to keep plugin self-contained.
- When generating new dialog types (Add/Edit/etc.), reference `MaterialLibraryWindow` as the baseline unless the user specifies otherwise.
