using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using LayerSync.UI.Views;

// The namespace must be unique
namespace LayerSync.Main
{
    public class Commands
    {
        // Static variable to hold the single instance of our window.
        private static LayerManagerWindow _layerWindow;

        [CommandMethod("LAYERSYNC")]
        public void ShowLayerSyncWindow()
        {
            // If the window is already created, just bring it to the front.
            if (_layerWindow != null)
            {
                _layerWindow.Activate();
                return;
            }

            // Create a new instance of the window.
            _layerWindow = new LayerManagerWindow();

            // Add an event handler to the window's Closed event.
            // When the window is closed, we set our static variable to null.
            _layerWindow.Closed += (s, e) => _layerWindow = null;

            // Show the window as modeless, parented to the AutoCAD main window.
            // This allows interaction with the drawing while the window is open.
            Application.ShowModelessWindow(_layerWindow);
        }
    }
}
