using System;
using System.Windows;

namespace LayerSync.UI.Core
{
    public static class ThemeManager
    {
        public static string CurrentTheme { get; private set; } = "Light";

        public static void ApplyTheme(Window window)
        {
            if (window == null) return;

            var themeName = CurrentTheme;
            var themePath = $"/LayerSync;component/UI/Themes/{themeName}Theme.xaml";
            var theme = new ResourceDictionary { Source = new Uri(themePath, UriKind.RelativeOrAbsolute) };

            window.Resources.MergedDictionaries.Clear();
            window.Resources.MergedDictionaries.Add(theme);
        }

        public static void ToggleTheme(Window window)
        {
            CurrentTheme = CurrentTheme == "Light" ? "Dark" : "Light";
            ApplyTheme(window);
        }
    }
}
