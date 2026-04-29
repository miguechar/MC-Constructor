using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Icon loader for ribbon buttons.
    ///
    /// To use a custom icon, drop a PNG named after the command at
    /// &lt;plugin-dll-folder&gt;\Images\&lt;CommandName&gt;.png. For example, the
    /// MCCreateProject button picks up Images\MCCreateProject.png.
    ///
    /// If no file is found we generate a clean placeholder: a rounded square
    /// in the panel's category color with the button's initials in white.
    /// That way the ribbon never has empty/missing-icon slots, but you can
    /// override any individual icon just by dropping a PNG in.
    /// </summary>
    public static class RibbonIcons
    {
        /// <summary>
        /// Resolve and cache the Images directory next to the loaded DLL.
        /// Done lazily because Assembly.Location can fail in odd hosting
        /// scenarios; we still want the synthesized fallback to work.
        /// </summary>
        private static string ImagesDir
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "Images");
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Load an icon for a button. Tries Images\&lt;key&gt;.png first;
        /// falls back to a synthesized icon with the initials of <paramref name="label"/>
        /// drawn on a rounded rectangle in <paramref name="fallbackColor"/>.
        /// </summary>
        /// <param name="key">Filename stem (typically the command name).</param>
        /// <param name="label">Button text - used to derive initials.</param>
        /// <param name="fallbackColor">Background color of the synthesized icon.</param>
        /// <param name="size">Icon size in pixels (use 32 for LargeImage, 16 for Image).</param>
        public static ImageSource Load(string key, string label, Color fallbackColor, int size)
        {
            return LoadFromDisk(key, size) ?? Generate(InitialsOf(label), fallbackColor, size);
        }

        private static ImageSource LoadFromDisk(string key, int size)
        {
            string dir = ImagesDir;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;

            string path = Path.Combine(dir, key + ".png");
            if (!File.Exists(path)) return null;

            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                img.UriSource = new Uri(path, UriKind.Absolute);
                // DecodePixel{Width,Height} let WPF render directly at the
                // ribbon's display size, which is sharper for icons.
                img.DecodePixelWidth = size;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Render a small rounded square in <paramref name="bgColor"/> with
        /// <paramref name="text"/> centered in white. Used as a fallback when
        /// no PNG was provided.
        /// </summary>
        public static ImageSource Generate(string text, Color bgColor, int size)
        {
            text = text ?? "?";

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = new SolidColorBrush(bgColor);
                bg.Freeze();
                double radius = size * 0.18;
                dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, size, size), radius, radius);

                // Subtle highlight on top half so the icon does not look flat.
                var highlight = new LinearGradientBrush(
                    Color.FromArgb(60, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0), new Point(0, 1));
                highlight.Freeze();
                dc.DrawRoundedRectangle(highlight, null, new Rect(0, 0, size, size), radius, radius);

                // emSize: bigger when only one character, smaller for two.
                double em = size * (text.Length == 1 ? 0.62 : 0.45);
                var typeface = new Typeface(
                    new FontFamily("Segoe UI"),
                    FontStyles.Normal,
                    FontWeights.Bold,
                    FontStretches.Normal);

                var ft = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    em,
                    Brushes.White,
                    pixelsPerDip: 1.0);

                var pt = new Point((size - ft.Width) / 2, (size - ft.Height) / 2);
                dc.DrawText(ft, pt);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// Two-letter initials derived from a button label. Strips newlines
        /// (button text often uses "\n" to wrap) and falls back to "?".
        /// </summary>
        private static string InitialsOf(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "?";
            var clean = label.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (string.IsNullOrEmpty(clean)) return "?";

            var words = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
                return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
            return char.ToUpperInvariant(words[0][0]).ToString();
        }
    }
}
