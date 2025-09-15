using System;
using System.Windows;

namespace LayerSync.UI.Core
{
    public static class ThemeManager
    {
        public static string CurrentTheme { get; private set; } = "Light";

        public static void ToggleTheme(Window window)
        {
            if (window == null) return;

            CurrentTheme = CurrentTheme == "Light" ? "Dark" : "Light";

            var themeName = CurrentTheme;
            var themePath = $"/LayerSync;component/UI/Themes/{themeName}Theme.xaml";
            var theme = new ResourceDictionary { Source = new Uri(themePath, UriKind.RelativeOrAbsolute) };

            // The first dictionary is the one we want to replace.
            // It was added in LayerManagerWindow.xaml
            if (window.Resources.MergedDictionaries.Count > 0)
            {
                window.Resources.MergedDictionaries[0] = theme;
            }
            else
            {
                window.Resources.MergedDictionaries.Add(theme);
            }
        }
    }
}
