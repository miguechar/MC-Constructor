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

### Label / TextBlock
- Foreground: #C8C8C8 (≈ (200,200,200))
- Alignment: VerticalCenter

### Button
- Background: `ButtonNormal`
- Foreground: White
- BorderThickness: 0
- Padding: (12,0,12,0)
- Height: 28
- Default or primary button uses `SelectionBlue` / white text
- **Disabled state:** Add a `Style` with an `IsEnabled = false` `Trigger` that sets `Opacity = 0.35`.
  Do NOT rely on `ButtonDisabledBG`/`ButtonDisabledFG` properties alone — WPF's Aero
  ControlTemplate ignores Background/Foreground in the disabled state and renders near-white,
  making text invisible. The Opacity approach is applied at the visual layer and always works.

```csharp
var style = new Style(typeof(Button));
var t = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
t.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
style.Triggers.Add(t);
button.Style = style;
```

### ComboBox
**Critical WPF behaviour:** The default Aero/Windows theme renders a ComboBox with a
**white background** in the selection area, ignoring the `Background` property set on the
control. Setting `Foreground = Brushes.White` therefore produces **white text on white
background — invisible.**

Rules:
- Set `Foreground = Brushes.Black` (readable on the white system-rendered background).
- Set `Background` to `SurfaceDark` anyway — it affects the dropdown popup area in some themes.
- For `ComboBoxItem` entries added at runtime, also set `Foreground = Brushes.Black` explicitly.
- Border: 1 px solid `Border`.

```csharp
private static ComboBox DarkCombo(int fontSize) => new ComboBox
{
    FontSize = fontSize,
    Padding = new Thickness(6, 3, 6, 3),
    Margin = new Thickness(0, 0, 0, 12),
    Background = D(37, 37, 38),
    Foreground = Brushes.Black,   // NOT White — Aero renders white background
    BorderBrush = D(67, 67, 70),
    BorderThickness = new Thickness(1),
};
```

### TreeView / TreeViewItem
**Critical WPF behaviour:** Setting `Foreground` on a `TreeView` does **not** propagate to
child `TreeViewItem` elements in the default Aero template. Each `TreeViewItem` must have
`Foreground` set explicitly or the text will be black (system default) on the dark background.

Rules:
- Set `Foreground = Brushes.White` on **every** `TreeViewItem` you create — group headers,
  discipline sub-headers, orphan groups, and leaf drawing items.
- Do not assume inheritance through the TreeView's Foreground property.

```csharp
var item = new TreeViewItem
{
    Header = title,
    Foreground = Brushes.White,   // required — not inherited from TreeView
    FontWeight = FontWeights.Bold,
    IsExpanded = true,
};
```

### DataGrid
- Background: `SurfaceDark`
- Row background: `WindowBackground`
- Alt row: `SurfaceLight`
- Grid lines: `Border`
- Column headers: slightly lighter than rows (`#37373C`)
- Selection highlight: `SelectionBlue`
- Apply `RowStyle`, `CellStyle`, and `ColumnHeaderStyle` via code-behind — WPF's DataGrid
  does propagate Foreground to rows, but setting it explicitly on each style is more reliable.

---

## General Layout
- Margin / Padding scale: 4–12 px typical
- Section headers: small caps or bold 10–11 pt gray text
- StackPanel spacing between buttons: 6 px

---

## Usage Guidance for Generation
- Always apply these colors when creating or modifying any WPF window, dialog, or control.
- Prefer programmatic brush definitions over external XAML to keep plugin self-contained.
- When generating new dialog types (Add/Edit/etc.), reference `MaterialLibraryWindow` as the
  baseline unless the user specifies otherwise.
- **Never assume** that setting `Background`/`Foreground` on a container control will propagate
  through WPF's default Aero ControlTemplates. ComboBox and TreeViewItem in particular must
  be styled explicitly as described above.
