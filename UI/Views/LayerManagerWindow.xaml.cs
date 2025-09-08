using LayerSync.UI.ViewModels;
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
    }
}

