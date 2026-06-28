using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoClicker
{
    /// <summary>
    /// Swaps the app's accent color at runtime by mutating the shared brush
    /// instances in App.xaml's resource dictionary (they're not frozen, so
    /// every control using {StaticResource AccentBrush} updates instantly).
    /// </summary>
    internal static class ThemeManager
    {
        private static DispatcherTimer? _rainbowTimer;
        private static double _hue = 0;

        public static readonly Dictionary<string, Color> Accents = new()
        {
            ["Blue"] = Color.FromRgb(0x6C, 0x8C, 0xFF),
            ["Green"] = Color.FromRgb(0x3D, 0xDC, 0x84),
            ["Purple"] = Color.FromRgb(0xB3, 0x88, 0xFF),
            ["Red"] = Color.FromRgb(0xFF, 0x5C, 0x5C),
            ["Orange"] = Color.FromRgb(0xFF, 0xB4, 0x54),
            ["Pink"] = Color.FromRgb(0xFF, 0x6F, 0xAE),
            ["Cyan"] = Color.FromRgb(0x4D, 0xD9, 0xE8),
            ["Gold"] = Color.FromRgb(0xFF, 0xD1, 0x66),
        };

        public static void Apply(string themeName)
        {
            StopRainbow();

            if (themeName == "Rainbow")
            {
                StartRainbow();
                return;
            }

            Color c = Accents.TryGetValue(themeName, out var col) ? col : Accents["Blue"];
            SetAccent(c);
        }

        private static void SetAccent(Color c)
        {
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(c);
            Application.Current.Resources["AccentBrushDim"] = new SolidColorBrush(Darken(c, 0.45));
        }

        private static Color Darken(Color c, double factor) =>
            Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

        private static void StartRainbow()
        {
            _rainbowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _rainbowTimer.Tick += (_, _) =>
            {
                _hue = (_hue + 1.5) % 360;
                SetAccent(HsvToRgb(_hue, 0.65, 1.0));
            };
            _rainbowTimer.Start();
        }

        private static void StopRainbow()
        {
            _rainbowTimer?.Stop();
            _rainbowTimer = null;
        }

        private static Color HsvToRgb(double h, double s, double v)
        {
            int hi = (int)(h / 60) % 6;
            double f = h / 60 - Math.Floor(h / 60);

            v *= 255;
            byte vByte = (byte)v;
            byte p = (byte)(v * (1 - s));
            byte q = (byte)(v * (1 - f * s));
            byte t = (byte)(v * (1 - (1 - f) * s));

            return hi switch
            {
                0 => Color.FromRgb(vByte, t, p),
                1 => Color.FromRgb(q, vByte, p),
                2 => Color.FromRgb(p, vByte, t),
                3 => Color.FromRgb(p, q, vByte),
                4 => Color.FromRgb(t, p, vByte),
                _ => Color.FromRgb(vByte, p, q),
            };
        }
    }
}
