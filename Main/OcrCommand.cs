using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Tesseract;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace LayerSync.Main
{
    public class OcrTextCommand
    {
        [CommandMethod("OCRTEXT")]
        public void OcrTextFromCurves()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptSelectionResult psr = ed.GetSelection();
                if (psr.Status != PromptStatus.OK) return;

                string outPath = Path.Combine(Path.GetTempPath(), "acad_ocr_capture.png");
                Extents3d totalExtents;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. Get entities and calculate their combined bounding box.
                    var entities = new List<Entity>();
                    totalExtents = new Extents3d();
                    bool hasExtents = false;

                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so?.ObjectId == null || so.ObjectId.IsErased) continue;
                        var ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (ent != null)
                        {
                            entities.Add(ent);
                            if (ent.Bounds.HasValue)
                            {
                                if (!hasExtents)
                                {
                                    totalExtents = ent.Bounds.Value;
                                    hasExtents = true;
                                }
                                else
                                {
                                    totalExtents.AddExtents(ent.Bounds.Value);
                                }
                            }
                        }
                    }

                    if (!hasExtents)
                    {
                        ed.WriteMessage("\nНе удалось получить границы выделенных объектов.");
                        return;
                    }

                    // 2. Render the entities to a PNG.
                    int bmpWidth = 2000, bmpHeight = 1500;
                    int padding = 100;

                    double worldWidth = totalExtents.MaxPoint.X - totalExtents.MinPoint.X;
                    double worldHeight = totalExtents.MaxPoint.Y - totalExtents.MinPoint.Y;

                    if (worldWidth < 1e-6 || worldHeight < 1e-6)
                    {
                        ed.WriteMessage("\nВыбранные объекты слишком малы для рендеринга.");
                        return;
                    }

                    double scaleX = (bmpWidth - 2 * padding) / worldWidth;
                    double scaleY = (bmpHeight - 2 * padding) / worldHeight;
                    double scale = Math.Min(scaleX, scaleY);


                    using (var bmp = new Bitmap(bmpWidth, bmpHeight))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        // Center the drawing in the bitmap
                        float transX = (float)(-totalExtents.MinPoint.X * scale + (bmpWidth - worldWidth * scale) / 2);
                        float transY = (float)(totalExtents.MaxPoint.Y * scale + (bmpHeight - worldHeight * scale) / 2);

                        g.TranslateTransform(transX, transY);
                        g.ScaleTransform((float)scale, (float)-scale);

                        using (var wd = new OcrWorldDraw(g))
                        {
                            foreach (var ent in entities)
                            {
                                ent.WorldDraw(wd);
                            }
                        }

                        bmp.Save(outPath, ImageFormat.Png);
                        ed.WriteMessage($"\nИзображение сохранено: {outPath}");
                    }

                    tr.Commit();
                }

                // 3. Perform OCR using Tesseract.
                string recognized = RunOcr(outPath, ed);

                if (string.IsNullOrWhiteSpace(recognized))
                {
                    ed.WriteMessage("\nТекст не распознан.");
                    return;
                }

                ed.WriteMessage($"\nРаспознанный текст:\n{recognized}");

                // 4. Insert the recognized text into the drawing.
                PromptPointResult ppr = ed.GetPoint("\nУкажите точку вставки текста: ");
                if (ppr.Status != PromptStatus.OK) return;

                using (var tr2 = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr2.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr2.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var mtext = new MText
                    {
                        Contents = recognized,
                        Location = ppr.Value,
                        TextHeight = (totalExtents.MaxPoint.Y - totalExtents.MinPoint.Y) / 20.0,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 1) // Red
                    };

                    btr.AppendEntity(mtext);
                    tr2.AddNewlyCreatedDBObject(mtext, true);
                    tr2.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nПроизошла ошибка: {ex.Message}");
                Debug.WriteLine(ex.ToString());
            }
        }

        private string RunOcr(string imagePath, Editor ed)
        {
            string result = "";
            try
            {
                // IMPORTANT: User must place 'tessdata' folder (containing rus.traineddata, eng.traineddata)
                // next to the compiled DLL. Download from: https://github.com/tesseract-ocr/tessdata_fast
                using (var engine = new TesseractEngine(@"./tessdata", "rus+eng", EngineMode.Default))
                {
                    using (var img = Pix.LoadFromFile(imagePath))
                    {
                        using (var page = engine.Process(img))
                        {
                            result = page.GetText();
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка OCR: {ex.Message}");
                if (ex.Message.Contains("Failed to find library"))
                    ed.WriteMessage("\nУбедитесь, что Visual C++ 2022 x64/x86 Runtimes установлены.");
                Debug.WriteLine($"OCR Error: {ex}");
            }
            finally
            {
                if (File.Exists(imagePath))
                {
                    File.Delete(imagePath);
                }
            }
            return result.Trim();
        }
    }

    public class OcrWorldDraw : WorldDraw
    {
        public OcrWorldGeometry OcrGeometry { get; }
        public OcrWorldDraw(Graphics g) { OcrGeometry = new OcrWorldGeometry(g); }
        public override WorldGeometry Geometry => OcrGeometry;
        public override bool RegenAbort => false;
    }

    public class OcrWorldGeometry : WorldGeometry
    {
        private readonly Graphics _graphics;
        private readonly Pen _pen = new Pen(System.Drawing.Color.Black, 0); // Cosmetic pen for consistent line width

        public OcrWorldGeometry(Graphics g) { _graphics = g; }

        private static PointF[] ToPointFArray(Point3dCollection points)
        {
            if (points == null || points.Count == 0) return new PointF[0];
            var pnts = new PointF[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                pnts[i] = new PointF((float)points[i].X, (float)points[i].Y);
            }
            return pnts;
        }

        public override void Polyline(int nbPoints, Point3dCollection points, Vector3d normal, IntPtr subEntity)
        {
            if (nbPoints < 2) return;
            _graphics.DrawLines(_pen, ToPointFArray(points));
        }

        public override void Circle(Point3d center, double radius, Vector3d normal)
        {
            var r = (float)radius;
            _graphics.DrawEllipse(_pen, (float)center.X - r, (float)center.Y - r, 2 * r, 2 * r);
        }

        public override void CircularArc(Point3d center, double radius, Vector3d normal, Vector3d startVector, double startAngle, double endAngle)
        {
            var sweep = (endAngle - startAngle) * (180.0 / Math.PI);
            var start = startAngle * (180.0 / Math.PI);

            if (sweep < 0)
            {
                start += sweep;
                sweep = -sweep;
            }
            var r = (float)radius;
            _graphics.DrawArc(_pen, (float)center.X - r, (float)center.Y - r, 2 * r, 2 * r, (float)start, (float)sweep);
        }
    }
}
