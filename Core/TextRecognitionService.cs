using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using Tesseract;
using Autodesk.AutoCAD.Geometry;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;

namespace LayerSync.Core
{
    public class TextRecognitionService
    {
        public List<Entity> ExtractGeometricEntities()
        {
            var entities = new List<Entity>();
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return entities;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                foreach (ObjectId objId in modelSpace)
                {
                    var entity = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;

                    // Check if the entity is one of the types we are interested in
                    if (entity is Line ||
                        entity is Polyline ||
                        entity is Spline ||
                        entity is Arc ||
                        entity is Circle)
                    {
                        // Important: We need a deep clone because the transaction will close.
                        entities.Add(entity.Clone() as Entity);
                    }
                }
                tr.Commit();
            }

            return entities;
        }

        public double CalculateClusteringTolerance(List<Entity> entities)
        {
            if (entities == null || entities.Count == 0) return 1.0;

            var validHeights = new List<double>();
            foreach (var entity in entities)
            {
                try
                {
                    var extents = entity.GeometricExtents;
                    // Check if extents are valid (e.g. not a point)
                    if (extents.MinPoint.X < extents.MaxPoint.X || extents.MinPoint.Y < extents.MaxPoint.Y)
                    {
                        var height = extents.MaxPoint.Y - extents.MinPoint.Y;
                        if (height > 1e-6)
                        {
                            validHeights.Add(height);
                        }
                    }
                }
                catch { /* Ignore entities that fail to provide extents */ }
            }

            if (validHeights.Count == 0) return 1.0; // Default if no entities with height found

            // Use the median height as a robust measure against outliers
            validHeights.Sort();
            var medianHeight = validHeights[validHeights.Count / 2];

            // Tolerance can be a fraction of the median character height.
            // This value may need tuning. 40% seems reasonable.
            return medianHeight * 0.4;
        }

        public List<List<Entity>> ClusterEntities(List<Entity> entities, double tolerance)
        {
            var clusters = new List<List<Entity>>();
            // Create a copy of the list to safely remove items from.
            var unclustered = new List<Entity>(entities);

            while (unclustered.Count > 0)
            {
                var currentCluster = new List<Entity>();
                var queue = new Queue<Entity>();

                // Start a new cluster from the first available entity.
                var startEntity = unclustered[0];
                queue.Enqueue(startEntity);
                currentCluster.Add(startEntity);
                unclustered.RemoveAt(0);

                while (queue.Count > 0)
                {
                    var currentSeed = queue.Dequeue();
                    var seedExtents = GetExpandedExtents(currentSeed, tolerance);
                    if (!AreExtentsValid(seedExtents)) continue;

                    // Find all nearby entities by iterating backwards (safe for removal).
                    for (int i = unclustered.Count - 1; i >= 0; i--)
                    {
                        var targetEntity = unclustered[i];
                        if (AreExtentsOverlapping(seedExtents, targetEntity.GeometricExtents))
                        {
                            currentCluster.Add(targetEntity);
                            unclustered.RemoveAt(i);
                            queue.Enqueue(targetEntity);
                        }
                    }
                }
                clusters.Add(currentCluster);
            }
            return clusters;
        }

        private Extents3d GetExpandedExtents(Entity entity, double tolerance)
        {
            try
            {
                var extents = entity.GeometricExtents;
                // Check for valid extents before expanding
                if (!AreExtentsValid(extents)) return new Extents3d();

                return new Extents3d(
                    new Point3d(extents.MinPoint.X - tolerance, extents.MinPoint.Y - tolerance, extents.MinPoint.Z),
                    new Point3d(extents.MaxPoint.X + tolerance, extents.MaxPoint.Y + tolerance, extents.MaxPoint.Z)
                );
            }
            catch
            {
                // Return an invalid/default Extents3d on any exception
                return new Extents3d();
            }
        }

        private bool AreExtentsOverlapping(Extents3d ext1, Extents3d ext2)
        {
            if (!AreExtentsValid(ext1) || !AreExtentsValid(ext2)) return false;

            // Standard Axis-Aligned Bounding Box (AABB) intersection test.
            return ext1.MinPoint.X <= ext2.MaxPoint.X && ext1.MaxPoint.X >= ext2.MinPoint.X &&
                   ext1.MinPoint.Y <= ext2.MaxPoint.Y && ext1.MaxPoint.Y >= ext2.MinPoint.Y;
        }

        private bool AreExtentsValid(Extents3d extents)
        {
            // Check if the extents represent a valid, non-point, non-inverted box.
            return extents.MinPoint.X <= extents.MaxPoint.X && extents.MinPoint.Y <= extents.MaxPoint.Y;
        }

        public List<RecognizedLine> RecognizeText(List<Entity> entities)
        {
            var recognizedLines = new List<RecognizedLine>();
            if (entities == null || entities.Count == 0) return recognizedLines;

            var tolerance = CalculateClusteringTolerance(entities);
            var clusters = ClusterEntities(entities, tolerance);

            var tessdataPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "tessdata");
            if (!Directory.Exists(tessdataPath) || !File.Exists(Path.Combine(tessdataPath, "eng.traineddata")))
            {
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[LayerSync OCR Error] 'tessdata' folder not found at {tessdataPath}");
                Application.ShowAlertDialog("Error: Tesseract language data not found.\nPlease ensure a 'tessdata' folder with 'eng.traineddata' exists in the same directory as the plugin DLL.");
                return recognizedLines;
            }

            using (var engine = new TesseractEngine(tessdataPath, "eng", EngineMode.Default))
            {
                engine.SetVariable("tessedit_char_whitelist", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz.,-+/\\°:;()[]{}<>_");

                var recognizedCharacters = new List<Tuple<Extents3d, string>>();
                foreach (var cluster in clusters)
                {
                    var character = RecognizeCluster(cluster, engine);
                    if (!string.IsNullOrWhiteSpace(character))
                    {
                        var clusterExtents = GetClusterExtents(cluster);
                        if (AreExtentsValid(clusterExtents))
                        {
                            recognizedCharacters.Add(new Tuple<Extents3d, string>(clusterExtents, character));
                        }
                    }
                }

                if (recognizedCharacters.Count == 0) return recognizedLines;

                // Sort characters into lines
                var sortedChars = recognizedCharacters
                    .OrderByDescending(c => Math.Round(c.Item1.MinPoint.Y / tolerance))
                    .ThenBy(c => c.Item1.MinPoint.X)
                    .ToList();

                // Group sorted characters by their calculated line group
                var lineGroups = sortedChars.GroupBy(c => Math.Round(c.Item1.MinPoint.Y / tolerance));

                foreach (var lineGroup in lineGroups)
                {
                    var lineChars = lineGroup.ToList();
                    if (lineChars.Count == 0) continue;

                    var sb = new StringBuilder();
                    var totalHeight = 0.0;

                    for (int i = 0; i < lineChars.Count; i++)
                    {
                        var current = lineChars[i];
                        sb.Append(current.Item2);
                        totalHeight += current.Item1.MaxPoint.Y - current.Item1.MinPoint.Y;

                        if (i < lineChars.Count - 1)
                        {
                            var next = lineChars[i + 1];
                            var gap = next.Item1.MinPoint.X - current.Item1.MaxPoint.X;
                            var spaceWidth = current.Item1.MaxPoint.X - current.Item1.MinPoint.X;
                            if (gap > spaceWidth * 0.4)
                            {
                                sb.Append(" ");
                            }
                        }
                    }

                    var avgHeight = totalHeight / lineChars.Count;
                    var insertPosition = lineChars[0].Item1.MinPoint;

                    recognizedLines.Add(new RecognizedLine
                    {
                        Text = sb.ToString(),
                        Position = new Point3d(insertPosition.X, insertPosition.Y, 0), // Ensure Z is 0
                        Height = avgHeight
                    });
                }

                return recognizedLines;
            }
        }

        private string RecognizeCluster(List<Entity> cluster, TesseractEngine engine)
        {
            if (cluster == null || cluster.Count == 0) return "";

            var extents = GetClusterExtents(cluster);
            if (!AreExtentsValid(extents)) return "";

            var width = extents.MaxPoint.X - extents.MinPoint.X;
            var height = extents.MaxPoint.Y - extents.MinPoint.Y;
            if (width < 1e-6 || height < 1e-6) return "";

            var padding = Math.Max(width, height) * 0.2;
            var imageSize = (int)Math.Ceiling(Math.Max(width, height) + 2 * padding);
            if (imageSize < 20) imageSize = 20;

            using (var bmp = new Bitmap(imageSize, imageSize))
            using (var gfx = Graphics.FromImage(bmp))
            {
                gfx.SmoothingMode = SmoothingMode.AntiAlias;
                gfx.Clear(Color.White);

                var transform = new Matrix();
                transform.Translate(-(float)(extents.MinPoint.X - padding), -(float)(extents.MinPoint.Y - padding));
                gfx.Transform = transform;

                using (var pen = new Pen(Color.Black, Math.Max(1, imageSize / 50f)))
                {
                    foreach (var entity in cluster)
                    {
                        DrawEntity(gfx, pen, entity);
                    }
                }

                using (var pix = Pix.LoadFromBitmap(bmp))
                {
                    using (var page = engine.Process(pix, PageSegMode.SingleChar))
                    {
                        return page.GetText().Trim();
                    }
                }
            }
        }

        private Extents3d GetClusterExtents(List<Entity> cluster)
        {
            if (cluster == null || cluster.Count == 0) return new Extents3d();
            var totalExtents = new Extents3d();
            bool first = true;
            foreach (var entity in cluster)
            {
                try
                {
                    var entityExtents = entity.GeometricExtents;
                    if (AreExtentsValid(entityExtents))
                    {
                        if (first) {
                            totalExtents.Set(entityExtents.MinPoint, entityExtents.MaxPoint);
                            first = false;
                        } else {
                            totalExtents.AddExtents(entityExtents);
                        }
                    }
                } catch { }
            }
            return totalExtents;
        }

        private void DrawEntity(Graphics gfx, Pen pen, Entity entity)
        {
            if (entity is Line line)
            {
                gfx.DrawLine(pen, ToPointF(line.StartPoint), ToPointF(line.EndPoint));
            }
            else if (entity is Circle circle)
            {
                var radius = (float)circle.Radius;
                gfx.DrawEllipse(pen, (float)circle.Center.X - radius, (float)circle.Center.Y - radius, 2 * radius, 2 * radius);
            }
            else if (entity is Arc arc)
            {
                var rect = new RectangleF((float)(arc.Center.X - arc.Radius), (float)(arc.Center.Y - arc.Radius), (float)(2 * arc.Radius), (float)(2 * arc.Radius));
                var startAngle = (float)(arc.StartAngle * 180 / Math.PI);
                var endAngle = (float)(arc.EndAngle * 180 / Math.PI);
                var sweepAngle = endAngle - startAngle;
                if (sweepAngle < 0) sweepAngle += 360;
                gfx.DrawArc(pen, rect, startAngle, sweepAngle);
            }
            else if (entity is Polyline pline)
            {
                if (pline.NumberOfVertices < 2) return;
                var points = new PointF[pline.NumberOfVertices];
                for (int i = 0; i < pline.NumberOfVertices; i++) {
                    points[i] = ToPointF(pline.GetPoint3dAt(i));
                }
                gfx.DrawLines(pen, points);
                if (pline.Closed && pline.NumberOfVertices > 2) {
                    gfx.DrawLine(pen, points[points.Length - 1], points[0]);
                }
            }
            else if (entity is Spline spline)
            {
                int numPoints = 20;
                var points = new PointF[numPoints + 1];
                for (int i = 0; i <= numPoints; i++)
                {
                    double param = spline.StartParam + (spline.EndParam - spline.StartParam) * i / numPoints;
                    points[i] = ToPointF(spline.GetPointAtParameter(param));
                }
                gfx.DrawLines(pen, points);
            }
        }

        private PointF ToPointF(Point3d pt)
        {
            return new PointF((float)pt.X, (float)pt.Y);
        }
    }
}
