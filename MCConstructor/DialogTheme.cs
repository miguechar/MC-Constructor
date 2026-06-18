using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCConstructor
{
    internal static class DialogTheme
    {
        private static readonly Brush PopupBackground = Brushes.White;
        private static readonly Brush PopupForeground = Brushes.Black;
        private static readonly Brush PopupHoverBackground = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private static readonly Brush DarkForeground = Brushes.White;
        private static readonly Brush DisabledForeground = new SolidColorBrush(Color.FromRgb(115, 115, 120));

        public static void Apply(Window window)
        {
            if (window == null) return;

            window.Resources[typeof(ComboBoxItem)] = ComboBoxItemStyle();
            window.Resources[typeof(ListBoxItem)] = ListBoxItemStyle();

            window.Loaded += (s, e) => ApplyToChildren(window);
        }

        private static Style ComboBoxItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, PopupBackground));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PopupForeground));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));

            var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, PopupHoverBackground));
            highlighted.Setters.Add(new Setter(Control.ForegroundProperty, DarkForeground));
            style.Triggers.Add(highlighted);

            var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, PopupHoverBackground));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, DarkForeground));
            style.Triggers.Add(selected);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, DisabledForeground));
            style.Triggers.Add(disabled);

            return style;
        }

        private static Style ListBoxItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, DarkForeground));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, PopupHoverBackground));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, DarkForeground));
            style.Triggers.Add(selected);

            return style;
        }

        private static void ApplyToChildren(DependencyObject root)
        {
            if (root == null) return;

            var combo = root as ComboBox;
            if (combo != null)
            {
                combo.ItemContainerStyle = combo.ItemContainerStyle ?? ComboBoxItemStyle();
                if (IsLight(combo.Background))
                    combo.Foreground = PopupForeground;
            }

            var textBox = root as TextBox;
            if (textBox != null)
            {
                textBox.Foreground = IsLight(textBox.Background) ? PopupForeground : DarkForeground;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
                ApplyToChildren(VisualTreeHelper.GetChild(root, i));
        }

        private static bool IsLight(Brush brush)
        {
            var solid = brush as SolidColorBrush;
            if (solid == null) return false;

            var c = solid.Color;
            return ((c.R * 299) + (c.G * 587) + (c.B * 114)) / 1000 > 160;
        }
    }
}
