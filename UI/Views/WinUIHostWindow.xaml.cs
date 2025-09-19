using LayerSync.UI.ViewModels;
using LayerSync.UI.WinUI;
using Microsoft.Toolkit.Wpf.UI.XamlHost;
using System.Windows;

namespace LayerSync.UI.Views
{
    public partial class WinUIHostWindow : Window
    {
        private LayerManagerViewModel _viewModel;

        public WinUIHostWindow()
        {
            InitializeComponent();
            MyXamlHost.ChildChanged += MyXamlHost_ChildChanged;
            this.Closed += OnWindowClosed;
        }

        private void MyXamlHost_ChildChanged(object sender, System.EventArgs e)
        {
            if (MyXamlHost.Child is LayerManagerControl winuiControl)
            {
                // Create the ViewModel and set it as the DataContext for the WinUI control
                _viewModel = new LayerManagerViewModel();
                winuiControl.DataContext = _viewModel;
            }
        }

        private void OnWindowClosed(object sender, System.EventArgs e)
        {
            // Call the cleanup method on the ViewModel when the window is closed
            _viewModel?.Cleanup();
        }
    }
}
