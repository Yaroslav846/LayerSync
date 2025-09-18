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
using Autodesk.AutoCAD.Colors;
using Color = Autodesk.AutoCAD.Colors.Color;

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
                        g.Clear(System.Drawing.Color.White);
                        g.SmoothingMode = SmoothingMode.AntiAlias;

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

                        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                        ed.WriteMessage($"\nИзображение сохранено: {outPath}");
                    }

                    tr.Commit();
                }

                string recognized = RunOcr(outPath, ed);

                if (string.IsNullOrWhiteSpace(recognized))
                {
                    ed.WriteMessage("\nТекст не распознан.");
                    return;
                }

                ed.WriteMessage($"\nРаспознанный текст:\n{recognized}");

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
                        Color = Color.FromColorIndex(ColorMethod.ByLayer, 1) // Red
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
        public readonly OcrWorldGeometry OcrGeometry;

        public OcrWorldDraw(Graphics g)
        {
            OcrGeometry = new OcrWorldGeometry(g);
        }

        public override WorldGeometry Geometry => OcrGeometry;
        public override WorldGeometry RawGeometry => OcrGeometry;
        public override SubEntityTraits Traits => SubEntityTraits;
        public override bool RegenAbort => false;
        public override RegenType RegenType => RegenType.Standard;
        public override bool IsDragging => false;
        public override int NumberOfIsolines => 16;
        public override double Deviation(DeviationType type, Point3d pt) => 0.0;
        public override Context Context => null;
    }

    public class OcrWorldGeometry : WorldGeometry
    {
        private readonly Graphics _graphics;
        private readonly Pen _pen = new Pen(System.Drawing.Color.Black, 0);

        public OcrWorldGeometry(Graphics g) { _graphics = g; }

        private static PointF[] ToPointFArray(Point3dCollection points)
        {
            if (points == null || points.Count == 0) return Array.Empty<PointF>();
            var pnts = new PointF[points.Count];
            for (int i = 0; i < points.Count; i++)
                pnts[i] = new PointF((float)points[i].X, (float)points[i].Y);
            return pnts;
        }

        public override bool Circle(Point3d center, double radius, Vector3d normal)
        {
            float r = (float)radius;
            _graphics.DrawEllipse(_pen, (float)center.X - r, (float)center.Y - r, 2 * r, 2 * r);
            return true;
        }

        public override bool CircularArc(Point3d center, double radius, Vector3d normal, Vector3d startVector, double sweepAngle, ArcType arcType)
        {
            double startAngle = new Vector3d(1, 0, 0).GetAngleTo(startVector, normal);
            float start = (float)(startAngle * 180.0 / Math.PI);
            float sweep = (float)(sweepAngle * 180.0 / Math.PI);
            float r = (float)radius;
            _graphics.DrawArc(_pen, (float)center.X - r, (float)center.Y - r, 2 * r, 2 * r, start, sweep);
            return true;
        }

        public override bool Polyline(Point3dCollection pts, Vector3d normal, IntPtr subEnt)
        {
            if (pts.Count < 2) return true;
             _graphics.DrawLines(_pen, ToPointFArray(pts));
            return true;
        }

        public override bool Polygon(Point3dCollection pts)
        {
            if (pts.Count < 2) return true;
            _graphics.DrawPolygon(_pen, ToPointFArray(pts));
            return true;
        }

        public override void SetExtents(Extents3d extents) { }
        public override bool PushClipBoundary(ClipBoundary boundary) { return false; }
        public override void PopClipBoundary() { }
        public override void PushModelTransform(Matrix3d xform) { }
        public override void PopModelTransform() { }
        public override bool Circle(Point3d p1, Point3d p2, Point3d p3) { return true; }
        public override bool CircularArc(Point3d start, Point3d point, Point3d end, ArcType arcType) { return true; }
        public override bool Polyline(Polyline polyline) { return true; }
        public override bool Mesh(int rows, int columns, Point3dCollection v, EdgeData e, FaceData f, VertexData d, bool gouraud) { return true; }
        public override bool Shell(Point3dCollection pts, IntegerCollection faces, EdgeData e, FaceData f, VertexData d, bool gouraud) { return true; }
        public override bool Text(Point3d pos, Vector3d normal, Vector3d dir, double height, double width, double oblique, string msg) { return true; }
        public override bool Text(Point3d pos, Vector3d normal, Vector3d dir, string msg, bool raw, TextStyle style) { return true; }
        public override bool Xline(Point3d p1, Point3d p2) { return true; }
        public override bool Ray(Point3d p1, Point3d p2) { return true; }
        public override bool EllipticalArc(Point3d center, Vector3d normal, double majorAxis, double minorAxis, double startAngle, double endAngle, double tilt, ArcType arcType) { return true; }
        public override bool WorldLine(Point3d start, Point3d end) { return true; }
        public override bool Polyline(Polyline polyline, int from, int to) { return true; }
        public override bool Polypoint(Point3dCollection pts, Vector3dCollection norms, IntPtrCollection subEntM) { return true; }
        public override bool PushPositionTransform(PositionBehavior behavior, Point3d offset) { return false; }
        public override bool PushPositionTransform(PositionBehavior behavior, Point2d offset) { return false; }
        public override bool PushScaleTransform(ScaleBehavior behavior, Point3d origin) { return false; }
        public override bool PushScaleTransform(ScaleBehavior behavior, Point2d origin) { return false; }
        public override bool PushOrientationTransform(OrientationBehavior behavior) { return false; }
        public override bool Draw(Drawable d) { return true; }
        public override bool Image(ImageBGRA32 image, Point3d origin, Vector3d u, Vector3d v, TransparencyMode mode) { return true; }
        public override bool RowOfDots(int count, Point3d start, Vector3d step) { return true; }
        public override bool Edge(Curve2dCollection edges) { return true; }
        public override void StartAttributesSegment() { }
        public override bool Curve(Curve3d curve) { return true; }
        public override bool PolyPolygon(UInt32Collection counts, Point3dCollection vertex, UInt32Collection faceCounts, Point3dCollection faceVertex, EntityColorCollection faceColors, LinetypeCollection faceLinetypes, EntityColorCollection edgeColors, TransparencyCollection faceTransparencies) { return true; }
        public override bool PolyPolyline(PolylineCollection polylines) { return true; }
        public override bool OwnerDraw(GdiDrawObject pObject, Point3d origin, Vector3d u, Vector3d v) { return true; }
        public override bool Image(ImageBGRA32 image, Point3d origin, Vector3d u, Vector3d v) { return true; }
        public override bool PushModelTransform(Vector3d normal) { return false; }
        public override Matrix3d ModelToWorldTransform => Matrix3d.Identity;
        public override Matrix3d WorldToModelTransform => Matrix3d.Identity;
    }
}
