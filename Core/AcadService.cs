using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Colors;
using LayerSync.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.GraphicsSystem;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using Point3d = Autodesk.AutoCAD.Geometry.Point3d;
using View = Autodesk.AutoCAD.GraphicsSystem.View;
using Tesseract;


namespace LayerSync.Core
{
    public static class AcadService
    {
        public static event EventHandler<string> LayerChanged;

        public static List<LayerItemViewModel> GetAllLayers()
        {
            var layers = new List<LayerItemViewModel>();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return layers;

            Database db = doc.Database;

            using (var transaction = db.TransactionManager.StartTransaction())
            {
                var layerTable = (LayerTable)transaction.GetObject(db.LayerTableId, OpenMode.ForRead);
                string currentLayerName = ((LayerTableRecord)transaction.GetObject(db.Clayer, OpenMode.ForRead)).Name;

                foreach (ObjectId layerId in layerTable)
                {
                    var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
                    layers.Add(new LayerItemViewModel
                    {
                        Name = layer.Name,
                        IsOn = !layer.IsOff,
                        IsFrozen = layer.IsFrozen,
                        AcadColor = layer.Color,
                        IsCurrent = layer.Name.Equals(currentLayerName, StringComparison.OrdinalIgnoreCase)
                    });
                }
                transaction.Commit();
            }
            return layers.OrderBy(l => l.Name).ToList();
        }

        public static Dictionary<string, int> GetObjectCountsForAllLayers()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return counts;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId objId in modelSpace)
                {
                    var entity = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (entity != null)
                    {
                        if (counts.ContainsKey(entity.Layer))
                        {
                            counts[entity.Layer]++;
                        }
                        else
                        {
                            counts[entity.Layer] = 1;
                        }
                    }
                }
                tr.Commit();
            }

            // Also ensure all layers from the layer table are in the dictionary, even if they have 0 objects.
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId layerId in layerTable)
                {
                    var layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
                    if (!counts.ContainsKey(layer.Name))
                    {
                        counts.Add(layer.Name, 0);
                    }
                }
                tr.Commit();
            }

            return counts;
        }

        public static void MoveSelectedObjectsToLayer(string targetLayerName)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            // Prompt the user to select objects
            var promptOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect objects to move to layer '" + targetLayerName + "': "
            };
            var selectionResult = ed.GetSelection(promptOptions);

            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
            {
                return; // User cancelled or no selection
            }

            var selectionSet = selectionResult.Value;
            var objectIds = selectionSet.GetObjectIds();

            if (objectIds.Length == 0)
            {
                return; // No objects in selection
            }

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // Verify the target layer exists
                    var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (!layerTable.Has(targetLayerName))
                    {
                        Application.ShowAlertDialog($"Error: Target layer '{targetLayerName}' does not exist.");
                        tr.Abort();
                        return;
                    }

                    foreach (ObjectId objId in objectIds)
                    {
                        try
                        {
                            var entity = tr.GetObject(objId, OpenMode.ForWrite) as Entity;
                            if (entity != null)
                            {
                                entity.Layer = targetLayerName;
                            }
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            // Log or show error for individual object if needed, but continue
                            ed.WriteMessage($"\nCould not move object {objId}: {ex.Message}");
                        }
                    }
                    tr.Commit();
                }
            }
        }

        public static void UpdateLayerProperty(string layerName, Action<LayerTableRecord> updateAction)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (layerTable.Has(layerName))
                    {
                        var layer = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForWrite);
                        updateAction(layer);
                    }
                    tr.Commit();
                }
            }
        }

        public static void BulkUpdateLayerProperties(IEnumerable<string> layerNames, Action<LayerTableRecord> updateAction)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    foreach (var layerName in layerNames)
                    {
                        if (layerTable.Has(layerName))
                        {
                            var layer = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForWrite);
                            updateAction(layer);
                        }
                    }
                    tr.Commit();
                }
            }
        }

        public static void SetCurrentLayer(string layerName)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (layerTable.Has(layerName))
                    {
                        db.Clayer = layerTable[layerName];
                    }
                    tr.Commit();
                }
            }
        }

        public static void HighlightEntitiesOnLayer(string layerName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            var objectIds = new ObjectIdCollection();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!string.IsNullOrEmpty(layerName))
                {
                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                    foreach (ObjectId objId in modelSpace)
                    {
                        var entity = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                        if (entity != null && entity.Layer.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                        {
                            objectIds.Add(objId);
                        }
                    }
                }

                // создаём SelectionSet и подсвечиваем
                if (objectIds.Count > 0)
                {
                    var selectionSet = SelectionSet.FromObjectIds(objectIds.Cast<ObjectId>().ToArray());
                    ed.SetImpliedSelection(selectionSet);
                }
                else
                {
                    ed.SetImpliedSelection(new ObjectId[0]); // снимаем выделение
                }

                tr.Commit();
            }
        }

        public static void HighlightEntitiesByColor(Color targetColor)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            var objectIds = new ObjectIdCollection();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                foreach (ObjectId objId in modelSpace)
                {
                    var entity = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;

                    Color effectiveColor = entity.Color;
                    if (effectiveColor.IsByLayer)
                    {
                        if (layerTable.Has(entity.Layer))
                        {
                            var layer = (LayerTableRecord)tr.GetObject(layerTable[entity.Layer], OpenMode.ForRead);
                            effectiveColor = layer.Color;
                        }
                    }

                    if (effectiveColor.Equals(targetColor))
                    {
                        objectIds.Add(objId);
                    }
                }

                if (objectIds.Count > 0)
                {
                    ed.SetImpliedSelection(objectIds.Cast<ObjectId>().ToArray());
                }
                else
                {
                    ed.SetImpliedSelection(new ObjectId[0]);
                }

                tr.Commit();
            }
        }


        private static void OnDatabaseObjectModified(object sender, ObjectEventArgs e)
        {
            if (e.DBObject is LayerTableRecord ltr)
            {
                LayerChanged?.Invoke(null, ltr.Name);
            }
        }

        public static void SubscribeToAcadEvents()
        {
            var db = HostApplicationServices.WorkingDatabase;
            db.ObjectModified += OnDatabaseObjectModified;
        }

        public static void UnsubscribeFromAcadEvents()
        {
            var db = HostApplicationServices.WorkingDatabase;
            db.ObjectModified -= OnDatabaseObjectModified;
        }

        public static bool CreateLayer(string layerName)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);

                    try
                    {
                        SymbolUtilityServices.ValidateSymbolName(layerName, false);
                        if (layerTable.Has(layerName))
                        {
                            Application.ShowAlertDialog($"Layer \"{layerName}\" already exists.");
                            return false;
                        }

                        var newLayer = new LayerTableRecord
                        {
                            Name = layerName
                        };
                        layerTable.Add(newLayer);
                        tr.AddNewlyCreatedDBObject(newLayer, true);
                        tr.Commit();
                        return true;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        Application.ShowAlertDialog($"Error creating layer: {ex.Message}");
                        tr.Abort();
                        return false;
                    }
                }
            }
        }

        public static void DeleteLayers(IEnumerable<string> layerNames)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    var layersToDelete = new ObjectIdCollection();
                    string currentLayerName = ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name;

                    foreach (var layerName in layerNames)
                    {
                        if (layerName.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                            layerName.Equals("Defpoints", StringComparison.OrdinalIgnoreCase) ||
                            layerName.Equals(currentLayerName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Skip protected layers
                        }

                        if (layerTable.Has(layerName))
                        {
                            var layerId = layerTable[layerName];
                            // A more robust check would verify if objects are on the layer.
                            // For simplicity, we'll rely on AutoCAD's native error for now.
                            layersToDelete.Add(layerId);
                        }
                    }

                    if (layersToDelete.Count > 0)
                    {
                        try
                        {
                            db.Purge(layersToDelete);
                            foreach (ObjectId id in layersToDelete)
                            {
                                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                                ltr.Erase();
                            }
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            Application.ShowAlertDialog($"One or more layers could not be deleted.\nThey may not be empty or may be referenced.\n\nDetails: {ex.Message}");
                        }
                    }

                    tr.Commit();
                }
            }
        }

        public static bool RenameLayer(string oldName, string newName)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                    if (layerTable.Has(oldName) && !layerTable.Has(newName))
                    {
                        try
                        {
                            var layer = (LayerTableRecord)tr.GetObject(layerTable[oldName], OpenMode.ForWrite);
                            SymbolUtilityServices.ValidateSymbolName(newName, false);
                            layer.Name = newName;
                            tr.Commit();
                            return true;
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            Application.ShowAlertDialog($"Error renaming layer: {ex.Message}");
                            tr.Abort();
                            return false;
                        }
                    }
                    else
                    {
                        if (layerTable.Has(newName))
                        {
                            Application.ShowAlertDialog($"Error: A layer with the name '{newName}' already exists.");
                        }
                        tr.Abort();
                        return false;
                    }
                }
            }
        }

        public static void ProcessTextRecognition(SelectionSet selectionSet, string language)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || selectionSet == null || selectionSet.Count == 0) return;

            string imagePath = null;
            try
            {
                imagePath = ExportObjectsToImage(selectionSet.GetObjectIds());
                if (string.IsNullOrEmpty(imagePath))
                {
                    doc.Editor.WriteMessage("\nFailed to export selection to image.");
                    return;
                }

                doc.Editor.WriteMessage($"\nSelection exported to temporary image: {imagePath}");

                string recognizedText = PerformOcr(imagePath, language);

                if (string.IsNullOrWhiteSpace(recognizedText))
                {
                    doc.Editor.WriteMessage("\nOCR did not recognize any text.");
                }
                else
                {
                    doc.Editor.WriteMessage($"\nRecognized Text: {recognizedText.Replace("\n", " ")}");

                    // Prompt user for insertion point
                    var ppo = new PromptPointOptions("\nSelect insertion point for the recognized text: ");
                    var ppr = doc.Editor.GetPoint(ppo);

                    if (ppr.Status == PromptStatus.OK)
                    {
                        InsertMText(recognizedText, ppr.Value);
                        doc.Editor.WriteMessage("\nText inserted successfully.");
                    }
                    else
                    {
                        doc.Editor.WriteMessage("\nText insertion cancelled.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nAn error occurred during text recognition: {ex.Message}");
            }
            finally
            {
                // Clean up the temporary file
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    File.Delete(imagePath);
                }
            }
        }

        private static string ExportObjectsToImage(ObjectId[] objectIds)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            Extents3d bounds = new Extents3d();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in objectIds)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        // Add the entity's bounds to the total bounds
                        // A null check on ent.Bounds is important as some entities might not have bounds.
                        if (ent.Bounds.HasValue)
                        {
                            bounds.AddExtents(ent.Bounds.Value);
                        }
                    }
                }
                tr.Commit();
            }

            if (!bounds.MinPoint.IsEqualTo(new Point3d()) && !bounds.MaxPoint.IsEqualTo(new Point3d()))
            {
                // We have valid bounds, proceed with export.
            }
            else
            {
                ed.WriteMessage("\nCould not determine the bounds of the selected objects.");
                return null;
            }

            // Use Graphics System to get a snapshot
            var gsManager = Manager.FromActiveViewport();
            if (gsManager == null)
            {
                ed.WriteMessage("\nCould not get Graphics System Manager.");
                return null;
            }

            // Create a GsView for the snapshot
            using (View view = new View())
            {
                gsManager.Add(view);

                // Set the view to the extents of the selection
                view.SetView(bounds.MinPoint, bounds.MaxPoint, new Autodesk.AutoCAD.Geometry.Vector3d(0, 0, 1), Autodesk.AutoCAD.Geometry.Vector3d.YAxis);
                view.RenderType = RenderType.Wireframe2D;
                view.VisualStyle = new VisualStyle(VisualStyleType.Wireframe);

                // Hide grid and other visual noise
                view.GridOn = false;
                view.ViewportBorderVisible = false;

                // High resolution for OCR
                int dpi = 300;
                double viewWidth = bounds.MaxPoint.X - bounds.MinPoint.X;
                double viewHeight = bounds.MaxPoint.Y - bounds.MinPoint.Y;

                // Maintain aspect ratio
                int imageWidth = (int)(viewWidth * dpi);
                int imageHeight = (int)(viewHeight * dpi);

                if (imageWidth == 0 || imageHeight == 0)
                {
                    ed.WriteMessage("\nCalculated image size is zero. Cannot export.");
                    return null;
                }

                using (var bmp = new Bitmap(imageWidth, imageHeight))
                {
                    using (var gfx = Graphics.FromImage(bmp))
                    {
                        // Set a white background
                        gfx.Clear(System.Drawing.Color.White);

                        // Get the snapshot
                        view.Viewport(gfx, new Rectangle(0, 0, imageWidth, imageHeight));
                    }

                    // Save to a temporary file
                    string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
                    bmp.Save(tempPath, ImageFormat.Png);

                    // Cleanup the GsView
                    gsManager.Erase(view);
                    gsManager.Update();

                    return tempPath;
                }
            }
        }

        private static string PerformOcr(string imagePath, string language)
        {
            // The Tesseract.Net.SDK automatically manages the tessdata.
            // We just need to specify the language. "eng" for English.
            // For Russian, it would be "rus". We can make this configurable later.
            try
            {
                // Note: The path to tessdata is handled automatically by the SDK if left null.
                using (var engine = new TesseractEngine(null, language, EngineMode.Default))
                {
                    using (var img = Pix.LoadFromFile(imagePath))
                    {
                        using (var page = engine.Process(img))
                        {
                            return page.GetText();
                        }
                    }
                }
            }
            catch (TesseractException ex)
            {
                // Log the Tesseract-specific error
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nOCR Error: {ex.Message}");
                return null;
            }
        }

        public static void InsertMText(string text, Point3d position)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                    using (var mtext = new MText())
                    {
                        mtext.Contents = text;
                        mtext.Location = position;
                        // Use current text style and a reasonable default height.
                        mtext.TextStyleId = db.Textstyle;
                        mtext.Height = 2.5;

                        modelSpace.AppendEntity(mtext);
                        tr.AddNewlyCreatedDBObject(mtext, true);
                    }

                    tr.Commit();
                }
            }
        }
    }
}

