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
            // Use the alias to specify the AutoCAD Application
            Document doc = AcadApplication.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // For production use, it's recommended to set the license key here
            // from a secure source like a config file or environment variable.
            // IronOcr.Installation.LicenseKey = "YOUR_LICENSE_KEY";

            // Modern IronOcr versions handle language pack installation automatically.
            // Ensure you have the necessary models available in your environment.

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
                try { clusterExtents.AddExtents(ent.GeometricExtents); }
                catch { /* Ignore entities that might not have valid extents */ }
            }

            ed.WriteMessage($"\nCluster bounding box: Min({clusterExtents.MinPoint.X:F2},{clusterExtents.MinPoint.Y:F2}) Max({clusterExtents.MaxPoint.X:F2},{clusterExtents.MaxPoint.Y:F2})");
            if (clusterExtents.MinPoint.X + 1e-6 > clusterExtents.MaxPoint.X ||
                clusterExtents.MinPoint.Y + 1e-6 > clusterExtents.MaxPoint.Y)
            {
                ed.WriteMessage("\nSkipping cluster with invalid or zero-area bounding box.");
                return "";
            }

            string tempPngFile = Path.ChangeExtension(Path.GetTempFileName(), ".png");
            ed.WriteMessage($"\nTemporary plot file: {tempPngFile}");

            try
            {
                LayoutManager lm = LayoutManager.Current;
                ed.WriteMessage("\nGot LayoutManager.");
                ObjectId layoutId = lm.GetLayoutId(lm.CurrentLayout);
                ed.WriteMessage($"\nGot current layout ID: {lm.CurrentLayout}.");

                using (var layout = (Layout)layoutId.GetObject(OpenMode.ForRead))
                {
                    ed.WriteMessage("\nOpened layout object.");
                    var plotInfo = new PlotInfo();
                    plotInfo.Layout = layout.Id;
                    ed.WriteMessage("\nCreated PlotInfo.");

                    var plotSettings = new PlotSettings(layout.ModelType);
                    plotSettings.CopyFrom(layout);
                    ed.WriteMessage("\nCreated PlotSettings and copied from layout.");

                    var psv = PlotSettingsValidator.Current;
                    ed.WriteMessage("\nGot PlotSettingsValidator.");

                    psv.SetPlotType(plotSettings, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                    ed.WriteMessage("\nSet PlotType to Window.");

                    psv.SetPlotWindowArea(plotSettings, new Extents2d(clusterExtents.MinPoint.X, clusterExtents.MinPoint.Y, clusterExtents.MaxPoint.X, clusterExtents.MaxPoint.Y));
                    ed.WriteMessage("\nSet PlotWindowArea.");

                    psv.SetPlotConfigurationName(plotSettings, "DWG To PNG.pc3", "PNG");
                    ed.WriteMessage("\nSet PlotConfigurationName to 'DWG To PNG.pc3'.");

                    psv.SetPlotCentered(plotSettings, true);
                    ed.WriteMessage("\nSet PlotCentered.");

                    psv.SetPlotRotation(plotSettings, PlotRotation.Degrees000);
                    ed.WriteMessage("\nSet PlotRotation.");

                    psv.SetStdScaleType(plotSettings, StdScaleType.ScaleToFit);
                    ed.WriteMessage("\nSet StdScaleType to ScaleToFit.");

                    psv.SetCurrentStyleSheet(plotSettings, "monochrome.ctb");
                    ed.WriteMessage("\nSet CurrentStyleSheet to 'monochrome.ctb'.");

                    plotInfo.OverrideSettings = plotSettings;
                    ed.WriteMessage("\nOverrode plot settings.");

                    var plotInfoValidator = new PlotInfoValidator();
                    plotInfoValidator.Validate(plotInfo);
                    ed.WriteMessage("\nPlotInfo validation successful.");

                    if (PlotFactory.ProcessPlotState == ProcessPlotState.NotPlotting)
                    {
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
                    }
                }

                if (File.Exists(tempPngFile))
                {
                    ed.WriteMessage("\nImage file created. Starting OCR...");
                    using (var ocrInput = new OcrInput(tempPngFile))
                    {
                        var result = ocr.Read(ocrInput);
                        ed.WriteMessage($"\nOCR finished. Result: '{result.Text.Trim()}'");
                        return result.Text;
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n--- DETAILED ERROR ---");
                ed.WriteMessage($"\nError during OCR plotting: {ex.ToString()}");
                ed.WriteMessage($"\n--- END DETAILED ERROR ---");
            }
            finally
            {
                if (File.Exists(tempPngFile))
                {
                    File.Delete(tempPngFile);
                }
            }
            return "";
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
            double averageHeight = totalHeight / entities.Count;
            double tolerance = averageHeight * 1.5;

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
