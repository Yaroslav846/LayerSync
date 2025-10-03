using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Colors;
using LayerSync.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public static Dictionary<string, LayerMetrics> GetLayerMetrics()
        {
            var metrics = new Dictionary<string, LayerMetrics>(StringComparer.OrdinalIgnoreCase);
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return metrics;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId objId in modelSpace)
                {
                    if (tr.GetObject(objId, OpenMode.ForRead) is not Entity entity) continue;

                    var layerName = entity.Layer;
                    if (!metrics.TryGetValue(layerName, out var currentMetrics))
                    {
                        currentMetrics = new LayerMetrics();
                    }

                    currentMetrics.ObjectCount++;

                    if (entity is Curve curve)
                    {
                        try
                        {
                            currentMetrics.TotalLength += curve.GetDistanceAtParameter(curve.EndParam);
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            // Ignore curves that don't support length calculation (e.g., rays, xlines)
                            System.Diagnostics.Debug.WriteLine($"Could not get length for entity {entity.Id}: {ex.Message}");
                        }
                    }

                    metrics[layerName] = currentMetrics;
                }

                var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId layerId in layerTable)
                {
                    var layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
                    if (!metrics.ContainsKey(layer.Name))
                    {
                        metrics.Add(layer.Name, new LayerMetrics { ObjectCount = 0, TotalLength = 0 });
                    }
                }
                tr.Commit();
            }

            return metrics;
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
    }
}

