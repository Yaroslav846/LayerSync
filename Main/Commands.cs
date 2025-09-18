using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
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

        [CommandMethod("OCRTEXT")]
        public void OcrTextFromCurves()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Get user selection
            PromptSelectionResult psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK) return;

            // 2. Perform OCR
            string recognized = OcrService.OcrTextFromSelection(psr.Value);

            if (string.IsNullOrWhiteSpace(recognized))
            {
                ed.WriteMessage("\nText could not be recognized.");
                return;
            }

            ed.WriteMessage($"\nRecognized text:\n{recognized}");

            // 3. Prompt for insertion point
            PromptPointResult ppr = ed.GetPoint("\nSpecify text insertion point: ");
            if (ppr.Status != PromptStatus.OK) return;

            // 4. Insert text into the drawing
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                MText mtext = new MText
                {
                    Contents = recognized,
                    Location = ppr.Value,
                    TextHeight = 2.5 // This can be configured or prompted
                };

                btr.AppendEntity(mtext);
                tr.AddNewlyCreatedDBObject(mtext, true);

                tr.Commit();
            }
        }
    }
}

