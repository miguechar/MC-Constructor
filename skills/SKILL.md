# Skill: UI Consistency Enforcer

## Summary
Automatically ensures that any generated or modified WPF or WinForms user interfaces follow the project’s standard dark theme and component styling.

## Files
- `ui.md` — defines full color palette and control rules for dark theme.

## Usage
When the user requests to:
- Create a new window class, dialog, or editor.
- Add buttons, text boxes, labels, or grids in any WPF class.
- Style or restyle existing controls.

Then refer to `ui.md` to:
1. Apply appropriate brush values and control styles.
2. Maintain dark-theme consistency across all plugin components.

## Directives
- Prefer defining brushes through helper methods (`DarkBrush(byte r, byte g, byte b)`) or shared resources.
- Do not override these styles unless explicitly instructed (“use light theme”, “high contrast”, etc.).
- Always name the helper functions and variable names according to project conventions (e.g. `DarkBrush`, `DialogBtn`, `MakeBox`).
- If a new color or variation is needed, derive it via brightness adjustment of existing palette values.

## Example Prompt Triggers
- “Create a new WPF window form for editing beam properties.”
- “Add a confirmation dialog with the same dark style as the material library.”
- “Change all buttons to match our dark theme.”

## References
- [`ui.md`](./ui.md) — canonical color and style definitions.
