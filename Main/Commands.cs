using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using LayerSync.UI.Views;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;

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

        [CommandMethod("RECOGNIZETEXT")]
        public void RecognizeTextCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            // Create a selection filter to allow only specific entity types
            var filterList = new TypedValue[] {
                new TypedValue((int)DxfCode.Operator, "<or>"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "ARC"),
                new TypedValue((int)DxfCode.Start, "SPLINE"),
                new TypedValue((int)DxfCode.Operator, "</or>")
            };
            var filter = new SelectionFilter(filterList);

            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect geometric primitives to recognize as text: "
            };

            var result = ed.GetSelection(options, filter);

            if (result.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nSelection cancelled.");
                return;
            }

            SelectionSet selectionSet = result.Value;
            ed.WriteMessage($"\n{selectionSet.Count} objects selected for text recognition.");

            // Prompt for language
            var langOptions = new PromptStringOptions("\nEnter OCR language (e.g., eng, rus): ");
            langOptions.DefaultValue = "eng";
            langOptions.AllowSpaces = false;
            var langResult = ed.GetString(langOptions);

            if (langResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nLanguage selection cancelled.");
                return;
            }

            string language = langResult.StringResult;

            // Call the AcadService to process the selection
            Core.AcadService.ProcessTextRecognition(selectionSet, language);
        }
    }
}

