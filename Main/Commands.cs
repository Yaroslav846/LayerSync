using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using LayerSync.Core;
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

        [CommandMethod("RECOGNIZETEXT")]
        public void RecognizeTextCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\nStarting text recognition process...");

            var recognitionService = new TextRecognitionService();
            var entities = recognitionService.ExtractGeometricEntities();

            if (entities.Count == 0)
            {
                Application.ShowAlertDialog("No geometric entities found to process.");
                return;
            }

            ed.WriteMessage($"\nFound {entities.Count} geometric entities. Processing for recognition...");
            var recognizedLines = recognitionService.RecognizeText(entities);

            if (recognizedLines.Count == 0)
            {
                Application.ShowAlertDialog("Could not recognize any text from the geometry.");
                return;
            }

            ed.WriteMessage($"\nRecognition complete. Found {recognizedLines.Count} lines of text. Creating DBText entities...");

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                    foreach (var line in recognizedLines)
                    {
                        var dbText = new DBText
                        {
                            Position = line.Position,
                            Height = line.Height,
                            TextString = line.Text
                        };

                        modelSpace.AppendEntity(dbText);
                        tr.AddNewlyCreatedDBObject(dbText, true);
                    }
                    tr.Commit();
                }
            }

            ed.WriteMessage($"\nSuccessfully created {recognizedLines.Count} text entities.");
            Application.ShowAlertDialog($"Successfully created {recognizedLines.Count} text entities.");
        }
    }
}
