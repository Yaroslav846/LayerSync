using LayerSync.UI.Core;
﻿using LayerSync.UI.ViewModels;
using System.Windows;

namespace LayerSync.UI.Views
{
    /// <summary>
    /// Interaction logic for LayerManagerWindow.xaml
    /// </summary>
    public partial class LayerManagerWindow : Window
    {
        public LayerManagerWindow()
        {
            InitializeComponent();

            // Create the ViewModel instance
            var viewModel = new LayerManagerViewModel();

            // Set it as the DataContext for the window
            this.DataContext = viewModel;

            // NEW: Add an event handler for the window's Closed event.
            // This ensures that cleanup logic in the ViewModel is executed.
            this.Closed += (s, e) => viewModel.Cleanup();
        }

        private void LayersListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (this.DataContext is LayerManagerViewModel viewModel)
            {
                viewModel.UpdateSelection(LayersListView.SelectedItems);
            }
        }

        private void FrozenCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && this.DataContext is LayerManagerViewModel viewModel && checkBox.DataContext is LayerItemViewModel clickedItem)
            {
                bool newState = checkBox.IsChecked ?? false;
                viewModel.SetFrozenStateForSelection(clickedItem, newState);
            }
        }

        private void OnCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && this.DataContext is LayerManagerViewModel viewModel && checkBox.DataContext is LayerItemViewModel clickedItem)
            {
                bool newState = checkBox.IsChecked ?? false;
                viewModel.SetOnStateForSelection(clickedItem, newState);
            }
        }
    }
}

