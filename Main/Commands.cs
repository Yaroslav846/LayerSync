using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
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
    }

    public class OcrTextCommand
    {
        [CommandMethod("OCRTEXT")]
        public void OcrTextFromCurves()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptSelectionResult psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK || psr.Value.Count == 0) return;

            // Рассчитываем габариты выделенных объектов для корректного масштабирования
            Extents3d selectionExtents = new Extents3d();
            bool hasGeometry = false;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent != null && ent.Bounds.HasValue)
                    {
                        selectionExtents.AddExtents(ent.Bounds.Value);
                        hasGeometry = true;
                    }
                }
                tr.Commit();
            }

            if (!hasGeometry)
            {
                ed.WriteMessage("\nВыбранные объекты не имеют геометрии для рендеринга.");
                return;
            }

            // Используем временный файл для сохранения изображения
            string tempImagePath = System.IO.Path.GetTempFileName();
            System.IO.File.Delete(tempImagePath); // GetTempFileName создает файл, нам нужно только уникальное имя
            tempImagePath = System.IO.Path.ChangeExtension(tempImagePath, ".png");

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. Рендер выделенных объектов в PNG
                    int width = 2000, height = 2000;

                    using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(width, height))
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(System.Drawing.Color.White);

                        using (var wd = new WorldDrawGdi(g, width, height, selectionExtents))
                        {
                            foreach (SelectedObject so in psr.Value)
                            {
                                if (so == null) continue;
                                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                                if (ent != null)
                                {
                                    ent.WorldDraw(wd);
                                }
                            }
                        }

                        bmp.Save(tempImagePath, System.Drawing.Imaging.ImageFormat.Png);
                        ed.WriteMessage($"\nИзображение для распознавания сохранено: {tempImagePath}");
                    }

                    tr.Commit();
                }

                // 2. OCR через Tesseract
                string recognized = RunOcr(tempImagePath);

                if (string.IsNullOrWhiteSpace(recognized))
                {
                    ed.WriteMessage("\nТекст не распознан.");
                    return;
                }

                ed.WriteMessage($"\nРаспознанный текст:\n{recognized}");

                // 3. Вставка текста в чертёж
                PromptPointResult ppr = ed.GetPoint("\nУкажите точку вставки текста: ");
                if (ppr.Status != PromptStatus.OK) return;

                using (Transaction tr2 = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr2.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr2.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    MText mtext = new MText
                    {
                        Contents = recognized,
                        Location = ppr.Value,
                        TextHeight = 2.5 // Можно настроить
                    };

                    btr.AppendEntity(mtext);
                    tr2.AddNewlyCreatedDBObject(mtext, true);

                    tr2.Commit();
                }
            }
            finally
            {
                // Удаляем временный файл
                if (System.IO.File.Exists(tempImagePath))
                {
                    System.IO.File.Delete(tempImagePath);
                }
            }
        }

        private string RunOcr(string imagePath)
        {
            string result = "";
            // Важно: папка 'tessdata' с языковыми файлами (например, rus.traineddata)
            // должна находиться рядом с скомпилированным плагином (.dll)
            // или нужно указать полный путь к ней.
            string tessDataPath = @"./tessdata";
            if (!System.IO.Directory.Exists(tessDataPath))
            {
                 Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nПапка tessdata не найдена по пути: {System.IO.Path.GetFullPath(tessDataPath)}");
                 return "Ошибка: папка tessdata не найдена.";
            }

            try
            {
                using (var engine = new Tesseract.TesseractEngine(tessDataPath, "rus+eng", Tesseract.EngineMode.Default))
                {
                    using (var img = Tesseract.Pix.LoadFromFile(imagePath))
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
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nОшибка Tesseract: {ex.Message}");
                return $"Ошибка OCR: {ex.Message}";
            }

            return result.Trim();
        }
    }

    // =============== GDI-адаптеры для рендеринга геометрии AutoCAD ===============

    public class WorldDrawGdi : Autodesk.AutoCAD.GraphicsInterface.WorldDraw
    {
        private readonly System.Drawing.Graphics _graphics;
        private readonly int _width, _height;
        private readonly Extents3d _modelExtents;
        private readonly Matrix3d _transform;

        public WorldDrawGdi(System.Drawing.Graphics g, int width, int height, Extents3d modelExtents)
        {
            _graphics = g;
            _width = width;
            _height = height;
            _modelExtents = modelExtents;

            // Рассчитываем трансформацию, чтобы вписать extents в изображение
            double modelWidth = _modelExtents.MaxPoint.X - _modelExtents.MinPoint.X;
            double modelHeight = _modelExtents.MaxPoint.Y - _modelExtents.MinPoint.Y;

            if (modelWidth < 1e-6 || modelHeight < 1e-6)
            {
                _transform = Matrix3d.Identity;
                return;
            }

            double scaleX = _width / modelWidth;
            double scaleY = _height / modelHeight;
            double scale = System.Math.Min(scaleX, scaleY) * 0.95; // 95% для небольшого отступа

            // Центрируем изображение
            double transX = (_width - modelWidth * scale) / 2.0 - _modelExtents.MinPoint.X * scale;
            double transY = (_height + modelHeight * scale) / 2.0 + _modelExtents.MinPoint.Y * scale;

            _transform = Matrix3d.Scaling(scale, new Point3d(0, 0, 0))
                * Matrix3d.Displacement(new Vector3d(transX, transY, 0));
        }

        private System.Drawing.PointF ToScreenPoint(Point3d p)
        {
            var transformedPoint = p.TransformBy(_transform);
            // Инвертируем Y, т.к. в System.Drawing ось Y направлена вниз
            return new System.Drawing.PointF((float)transformedPoint.X, _height - (float)transformedPoint.Y);
        }

        public override Autodesk.AutoCAD.GraphicsInterface.WorldGeometry Geometry => new WorldGeometryGdi(this);
        public override bool RegenAbort => false;
        public override void SubEntityTraits(SubEntityTraits traits) { }
        public override Autodesk.AutoCAD.GraphicsInterface.WorldGeometry RawGeometry => Geometry;

        public class WorldGeometryGdi : Autodesk.AutoCAD.GraphicsInterface.WorldGeometry
        {
            private readonly WorldDrawGdi _owner;

            public WorldGeometryGdi(WorldDrawGdi owner)
            {
                _owner = owner;
            }

            private void DrawPolyline(Point3dCollection points)
            {
                if (points.Count < 2) return;

                System.Drawing.PointF[] screenPoints = new System.Drawing.PointF[points.Count];
                for (int i = 0; i < points.Count; i++)
                {
                    screenPoints[i] = _owner.ToScreenPoint(points[i]);
                }
                _owner._graphics.DrawLines(System.Drawing.Pens.Black, screenPoints);
            }

            public override void Polyline(PolylineData polyline)
            {
                using(var pts = new Point3dCollection())
                {
                    polyline.GetPoints(pts);
                    DrawPolyline(pts);
                }
            }

            public override void Circle(Point3d center, double radius, Vector3d normal)
            {
                // Аппроксимируем круг полилинией
                int segments = 64;
                var points = new Point3dCollection();
                double angleStep = 2 * System.Math.PI / segments;
                for (int i = 0; i <= segments; i++)
                {
                    double angle = i * angleStep;
                    Point3d p = center + new Vector3d(radius * System.Math.Cos(angle), radius * System.Math.Sin(angle), 0);
                    points.Add(p);
                }
                DrawPolyline(points);
            }

            public override void CircularArc(Point3d center, double radius, Vector3d normal, Vector3d startVector, double sweepAngle, ArcType arcType)
            {
                 // Аппроксимируем дугу
                int segments = (int)(System.Math.Abs(sweepAngle) / (2 * System.Math.PI) * 64) + 1;
                var points = new Point3dCollection();
                double startAngle = System.Math.Atan2(startVector.Y, startVector.X);

                for (int i = 0; i <= segments; i++)
                {
                    double angle = startAngle + (sweepAngle / segments) * i;
                    Point3d p = center + new Vector3d(radius * System.Math.Cos(angle), radius * System.Math.Sin(angle), 0);
                    points.Add(p);
                }
                DrawPolyline(points);
            }

            public override void Polygon(int nbPoints, Point3dCollection points)
            {
                if (nbPoints < 3) return;
                System.Drawing.PointF[] screenPoints = new System.Drawing.PointF[nbPoints];
                for (int i = 0; i < nbPoints; i++)
                {
                    screenPoints[i] = _owner.ToScreenPoint(points[i]);
                }
                _owner._graphics.DrawPolygon(System.Drawing.Pens.Black, screenPoints);
            }

            public override void Polyline(int nbPoints, Point3dCollection points, Vector3d normal, System.IntPtr pSubEntityTraits)
            {
                DrawPolyline(points);
            }
        }
    }
}

