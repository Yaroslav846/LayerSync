using LayerSync.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LayerSync.UI.WinUI
{
    public sealed partial class LayerManagerControl : UserControl
    {
        public LayerManagerControl()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.Dispatcher = this.DispatcherQueue;
                // Assign the theme toggle logic to the ViewModel's command
                ViewModel.ToggleThemeCommand = new RelayCommand(ToggleTheme);
            }
        }

        public LayerManagerViewModel ViewModel => DataContext as LayerManagerViewModel;

        private void ToggleTheme()
        {
            if (this.XamlRoot.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = rootElement.RequestedTheme == ElementTheme.Dark
                    ? ElementTheme.Light
                    : ElementTheme.Dark;
            }
        }

        private void LayersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ViewModel?.UpdateSelection(LayersDataGrid.SelectedItems);
        }

        private void FrozenCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && ViewModel != null && checkBox.DataContext is LayerItemViewModel clickedItem)
            {
                bool newState = checkBox.IsChecked ?? false;
                ViewModel.SetFrozenStateForSelection(clickedItem, newState);
            }
        }

        private void OnCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && ViewModel != null && checkBox.DataContext is LayerItemViewModel clickedItem)
            {
                bool newState = checkBox.IsChecked ?? false;
                ViewModel.SetOnStateForSelection(clickedItem, newState);
            }
        }
    }
}
