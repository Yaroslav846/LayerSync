using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using IronOcr;
using LayerSync.UI.Views;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

// To avoid ambiguity with System.Windows.Application, we can use an alias.
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LayerSync.Main
{
    public class Commands
    {
        private static LayerManagerWindow _layerWindow;

        [CommandMethod("LAYERSYNC")]
        public void ShowLayerSyncWindow()
        {
            if (_layerWindow != null) { _layerWindow.Activate(); return; }
            _layerWindow = new LayerManagerWindow();
            _layerWindow.Closed += (s, e) => _layerWindow = null;
            // Use the alias to specify the AutoCAD Application
            AcadApplication.ShowModelessWindow(_layerWindow);
        }

        [CommandMethod("RECOGNIZETEXT")]
        public void RecognizeText()
        {
            Document doc = AcadApplication.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            TypedValue[] filterList = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<or"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "SPLINE"),
                new TypedValue((int)DxfCode.Operator, "or>")
            };
            SelectionFilter filter = new SelectionFilter(filterList);
            PromptSelectionOptions opts = new PromptSelectionOptions();
            opts.MessageForAdding = "\nSelect lines, polylines, and splines to recognize as text: ";
            PromptSelectionResult selRes = ed.GetSelection(opts, filter);

            if (selRes.Status != PromptStatus.OK) { ed.WriteMessage("\nSelection cancelled."); return; }

            SelectionSet selSet = selRes.Value;
            if (selSet == null || selSet.Count == 0) { ed.WriteMessage("\nNo entities selected."); return; }

            var ocr = new IronTesseract();
            ocr.Language = OcrLanguage.Russian;
            ocr.AddSecondaryLanguage(OcrLanguage.English);
            ed.WriteMessage($"\nInitialized OCR engine with languages: {ocr.Language}, English.");

            StringBuilder recognizedTextBuilder = new StringBuilder();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<Entity> entities = selSet.GetObjectIds()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead) as Entity)
                    .Where(ent => ent != null)
                    .ToList();

                if (entities.Any())
                {
                    List<List<Entity>> clusters = ClusterEntities(entities);
                    ed.WriteMessage($"\nFound {clusters.Count} text clusters. Processing...");

                    int clusterNum = 1;
                    foreach (var cluster in clusters)
                    {
                        ed.WriteMessage($"\n--- Processing cluster {clusterNum++} of {clusters.Count} ---");
                        string text = RecognizeTextFromCluster(cluster, doc, ocr);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            recognizedTextBuilder.AppendLine(text);
                        }
                    }
                }
                tr.Commit();
            }

            string finalText = recognizedTextBuilder.ToString().Trim();
            ed.WriteMessage($"\n--- FINAL RECOGNIZED TEXT ---\n{finalText}\n------------------------------");
            if (string.IsNullOrWhiteSpace(finalText))
            {
                MessageBox.Show("Could not recognize any text.", "Recognize Text");
            }
            else
            {
                Clipboard.SetText(finalText);
                MessageBox.Show($"Recognized text:\n\n{finalText}\n\n(The text has been copied to your clipboard)", "Recognize Text Success");
            }
        }

        private string RecognizeTextFromCluster(List<Entity> cluster, Document doc, IronTesseract ocr)
        {
            Editor ed = AcadApplication.DocumentManager.MdiActiveDocument.Editor;
            if (cluster == null || !cluster.Any()) return "";

            var clusterExtents = new Extents3d();
            foreach (var ent in cluster)
            {
                try {
                    if (ent.GeometricExtents.MinPoint.X < ent.GeometricExtents.MaxPoint.X)
                    {
                        clusterExtents.AddExtents(ent.GeometricExtents);
                    }
                }
                catch { /* Ignore entities that might not have valid extents */ }
            }

            ed.WriteMessage($"\nCluster bounding box: Min({clusterExtents.MinPoint.X:F2},{clusterExtents.MinPoint.Y:F2}) Max({clusterExtents.MaxPoint.X:F2},{clusterExtents.MaxPoint.Y:F2})");
            if (clusterExtents.MinPoint.X + 1e-6 > clusterExtents.MaxPoint.X ||
                clusterExtents.MinPoint.Y + 1e-6 > clusterExtents.MaxPoint.Y)
            {
                ed.WriteMessage("\nSkipping cluster with invalid or zero-area bounding box.");
                return "";
            }

            string tempPngFile = null;
            try
            {
                tempPngFile = PlotClusterToPng(clusterExtents, doc);

                if (string.IsNullOrEmpty(tempPngFile) || !File.Exists(tempPngFile))
                {
                    ed.WriteMessage("\nPlotting failed or produced no file, skipping OCR for this cluster.");
                    return "";
                }

                ed.WriteMessage("\nImage file created. Starting OCR...");
                using (var ocrInput = new OcrInput(tempPngFile))
                {
                    var result = ocr.Read(ocrInput);
                    ed.WriteMessage($"\nOCR finished. Result: '{result.Text.Trim()}'");
                    return result.Text;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n--- DETAILED ERROR during text recognition cluster processing ---\n{ex.ToString()}\n--- END DETAILED ERROR ---");

                string message = $"An unexpected error occurred while trying to process a text cluster.\n\n" +
                                 $"Error: {ex.Message}\n\n" +
                                 "Please check the AutoCAD command line for a detailed log. " +
                                 "Common causes for this error include:\n" +
                                 " - Missing plotter: 'DWG To PNG.pc3'\n" +
                                 " - Missing plot style: 'monochrome.ctb'\n" +
                                 " - File system permission issues.";

                MessageBox.Show(message, "Text Recognition Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPngFile) && File.Exists(tempPngFile))
                {
                    try { File.Delete(tempPngFile); }
                    catch (System.Exception ex) { ed.WriteMessage($"\nFailed to delete temp file {tempPngFile}: {ex.Message}"); }
                }
            }
            return "";
        }

        private string PlotClusterToPng(Extents3d clusterExtents, Document doc)
        {
            Editor ed = doc.Editor;
            string tempPngFile = Path.ChangeExtension(Path.GetTempFileName(), ".png");
            ed.WriteMessage($"\nAttempting to plot cluster to temporary file: {tempPngFile}");

            LayoutManager lm = LayoutManager.Current;
            string originalLayout = lm.CurrentLayout;
            ed.WriteMessage($"\nOriginal layout is '{originalLayout}'. Switching to 'Model' for plotting to ensure a clean context.");

            try
            {
                lm.CurrentLayout = "Model";

                var psv = PlotSettingsValidator.Current;

                const string requiredPlotter = "DWG To PNG.pc3";
                if (!psv.GetPlotDeviceList().Cast<string>().Contains(requiredPlotter, StringComparer.OrdinalIgnoreCase))
                {
                    string msg = $"Required plotter configuration '{requiredPlotter}' was not found. Please configure it in AutoCAD's plot manager.";
                    MessageBox.Show(msg, "Plotter Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    ed.WriteMessage($"\nERROR: {msg}");
                    return null;
                }

                const string requiredStyleSheet = "monochrome.ctb";
                if (!psv.GetPlotStyleSheetList().Cast<string>().Contains(requiredStyleSheet, StringComparer.OrdinalIgnoreCase))
                {
                    string msg = $"Required plot style table '{requiredStyleSheet}' was not found. Please ensure it is in AutoCAD's support paths.";
                    MessageBox.Show(msg, "Plot Style Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    ed.WriteMessage($"\nERROR: {msg}");
                    return null;
                }

                ObjectId layoutId = lm.GetLayoutId("Model");

                using (var layout = (Layout)layoutId.GetObject(OpenMode.ForRead))
                using (var plotInfo = new PlotInfo())
                using (var plotSettings = new PlotSettings(layout.ModelType))
                {
                    plotInfo.Layout = layout.Id;

                    psv.SetPlotConfigurationName(plotSettings, requiredPlotter, null);
                    psv.SetPlotType(plotSettings, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                    psv.SetPlotWindowArea(plotSettings, new Extents2d(clusterExtents.MinPoint.X, clusterExtents.MinPoint.Y, clusterExtents.MaxPoint.X, clusterExtents.MaxPoint.Y));
                    psv.SetPlotCentered(plotSettings, true);
                    psv.SetPlotRotation(plotSettings, PlotRotation.Degrees000);
                    psv.SetStdScaleType(plotSettings, StdScaleType.ScaleToFit);
                    psv.SetCurrentStyleSheet(plotSettings, requiredStyleSheet);

                    plotInfo.OverrideSettings = plotSettings;
                    var plotInfoValidator = new PlotInfoValidator();
                    plotInfoValidator.Validate(plotInfo);

                    if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                    {
                        ed.WriteMessage("\nPlot engine is busy. Cannot plot cluster at this time.");
                        return null;
                    }

                    ed.WriteMessage("\nPlot engine is not busy. Starting plot...");
                    using (var plotEngine = PlotFactory.CreatePublishEngine())
                    {
                        plotEngine.BeginPlot(null, null);
                        plotEngine.BeginDocument(plotInfo, doc.Name, null, 1, true, tempPngFile);
                        plotEngine.BeginPage(new PlotPageInfo(), plotInfo, true, null);
                        plotEngine.BeginGenerateGraphics(null);
                        plotEngine.EndGenerateGraphics(null);
                        plotEngine.EndPage(null);
                        plotEngine.EndDocument(null);
                        plotEngine.EndPlot(null);
                    }
                    ed.WriteMessage("\nPlotting complete.");
                    return tempPngFile;
                }
            }
            finally
            {
                if (lm.CurrentLayout != originalLayout)
                {
                    ed.WriteMessage($"\nSwitching back to original layout '{originalLayout}'.");
                    lm.CurrentLayout = originalLayout;
                }
            }
        }

        private bool ExtentsOverlap(Extents3d ext1, Extents3d ext2)
        {
            return ext1.MinPoint.X <= ext2.MaxPoint.X && ext1.MaxPoint.X >= ext2.MinPoint.X &&
                   ext1.MinPoint.Y <= ext2.MaxPoint.Y && ext1.MaxPoint.Y >= ext2.MinPoint.Y;
        }

        private List<List<Entity>> ClusterEntities(List<Entity> entities)
        {
            var clusters = new List<List<Entity>>();
            if (!entities.Any()) return clusters;

            var entityExtents = entities.ToDictionary(e => e, e => e.GeometricExtents);

            if (!entities.Any()) return clusters;
            double totalHeight = entityExtents.Values.Sum(ext => ext.MaxPoint.Y - ext.MinPoint.Y);
            double averageHeight = entities.Count > 0 ? totalHeight / entities.Count : 0;
            double tolerance = averageHeight * 1.5;

            var ed = AcadApplication.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage($"\nClustering with average height: {averageHeight:F2}, tolerance: {tolerance:F2}.");

            var remainingEntities = new List<Entity>(entities);

            while (remainingEntities.Any())
            {
                var currentCluster = new List<Entity>();
                var clusterExtents = new Extents3d();

                var seed = remainingEntities[0];
                remainingEntities.RemoveAt(0);
                currentCluster.Add(seed);
                clusterExtents.AddExtents(entityExtents[seed]);

                int lastCount;
                do
                {
                    lastCount = remainingEntities.Count;
                    var foundInThisPass = new List<Entity>();

                    foreach (var entity in remainingEntities)
                    {
                        var testExtents = entityExtents[entity];
                        var expandedClusterExtents = new Extents3d(
                            new Point3d(clusterExtents.MinPoint.X - tolerance, clusterExtents.MinPoint.Y - tolerance, 0),
                            new Point3d(clusterExtents.MaxPoint.X + tolerance, clusterExtents.MaxPoint.Y + tolerance, 0));

                        if (ExtentsOverlap(expandedClusterExtents, testExtents))
                        {
                            foundInThisPass.Add(entity);
                        }
                    }

                    if (foundInThisPass.Any())
                    {
                        foreach (var foundEntity in foundInThisPass)
                        {
                            currentCluster.Add(foundEntity);
                            clusterExtents.AddExtents(entityExtents[foundEntity]);
                            remainingEntities.Remove(foundEntity);
                        }
                    }
                } while (remainingEntities.Count < lastCount);

                clusters.Add(currentCluster);
            }

            return clusters;
        }
    }
}
