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

        public static void RenameLayer(string oldName, string newName)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
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
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            Application.ShowAlertDialog($"Error renaming layer: {ex.Message}");
                            tr.Abort();
                        }
                    }
                    else
                    {
                        if (layerTable.Has(newName))
                        {
                            Application.ShowAlertDialog($"Error: A layer with the name '{newName}' already exists.");
                        }
                        tr.Abort();
                    }
                }
            }
        }
    }
}

