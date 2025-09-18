using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LayerSync.Core
{
    #region Data Structures for Recognition

    public readonly struct NormalizedSegment
    {
        public Point2d Start { get; }
        public Point2d End { get; }

        public NormalizedSegment(Point2d start, Point2d end)
        {
            if (start.X > end.X || (Math.Abs(start.X - end.X) < 1e-9 && start.Y > end.Y))
            {
                Start = end;
                End = start;
            }
            else
            {
                Start = start;
                End = end;
            }
        }
    }

    public class CharacterTemplate
    {
        public string Character { get; }
        public List<NormalizedSegment> Segments { get; }

        public CharacterTemplate(string character, List<NormalizedSegment> segments)
        {
            Character = character;
            Segments = segments;
        }
    }

    #endregion

    public class TextRecognizer
    {
        #region Private Helper Classes

        private class TextCreationInfo
        {
            public string Text { get; set; }
            public Point3d Position { get; set; }
            public double Height { get; set; }
            public string Layer { get; set; }
        }

        #endregion

        private static List<CharacterTemplate> _characterTemplates;
        private const double RecognitionThreshold = 0.15; // Tuned for the new comparison logic

        public void RecognizeText()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            ed.WriteMessage("\nStarting text recognition...");

            InitializeTemplates();

            List<ObjectId> allCurveIds = GetAllCurveIds(db);
            ed.WriteMessage($"\nFound {allCurveIds.Count} curve entities.");

            if (allCurveIds.Count == 0)
            {
                ed.WriteMessage("\nText recognition finished.");
                return;
            }

            List<List<ObjectId>> clusters = GroupCurves(db, allCurveIds);
            ed.WriteMessage($"\nGrouped into {clusters.Count} potential character clusters.");

            var textToCreate = new List<TextCreationInfo>();
            var idsToDelete = new List<ObjectId>();

            foreach (var cluster in clusters)
            {
                string recognizedChar = RecognizeCluster(db, cluster);
                if (!string.IsNullOrEmpty(recognizedChar))
                {
                    ed.WriteMessage($"\n  Cluster of {cluster.Count} curves recognized as: '{recognizedChar}'");

                    using(var tr = db.TransactionManager.StartTransaction())
                    {
                        var bounds = GetClusterBounds(tr, cluster);
                        if(bounds.MinPoint == bounds.MaxPoint) continue;

                        double height = bounds.MaxPoint.Y - bounds.MinPoint.Y;
                        var position = new Point3d(
                            (bounds.MinPoint.X + bounds.MaxPoint.X) / 2.0,
                            bounds.MinPoint.Y,
                            0
                        );
                        string layer = tr.GetObject(cluster.First(), OpenMode.ForRead).Layer;

                        textToCreate.Add(new TextCreationInfo
                        {
                            Text = recognizedChar,
                            Position = position,
                            Height = height,
                            Layer = layer
                        });

                        idsToDelete.AddRange(cluster);
                        tr.Commit();
                    }
                }
                else
                {
                    ed.WriteMessage($"\n  Cluster of {cluster.Count} curves could not be recognized.");
                }
            }

            if (textToCreate.Any())
            {
                CreateDbText(db, textToCreate);
                ed.WriteMessage($"\nCreated {textToCreate.Count} new text objects.");

                DeleteEntities(db, idsToDelete, ed);
                ed.WriteMessage($"\nDeleted {idsToDelete.Count} original geometric entities.");
            }

            ed.WriteMessage("\nText recognition finished.");
        }

        #region Database Modification

        private void CreateDbText(Database db, List<TextCreationInfo> textInfos)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                foreach (var info in textInfos)
                {
                    var dbText = new DBText
                    {
                        TextString = info.Text,
                        Position = info.Position,
                        Height = info.Height,
                        Layer = info.Layer,
                        HorizontalMode = TextHorizontalMode.TextCenter,
                        Justify = AttachmentPoint.BottomCenter
                    };

                    modelSpace.AppendEntity(dbText);
                    tr.AddNewlyCreatedDBObject(dbText, true);
                }
                tr.Commit();
            }
        }

        private void DeleteEntities(Database db, List<ObjectId> idsToDelete, Editor ed)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in idsToDelete)
                {
                    try
                    {
                        var obj = tr.GetObject(id, OpenMode.ForWrite);
                        obj.Erase();
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        ed.WriteMessage($"\nCould not delete object with id {id}: {ex.Message}");
                    }
                }
                tr.Commit();
            }
        }

        private Extents3d GetClusterBounds(Transaction tr, List<ObjectId> clusterIds)
        {
            var bounds = new Extents3d();
            if (!clusterIds.Any()) return bounds;

            try
            {
                var firstCurve = (Curve)tr.GetObject(clusterIds.First(), OpenMode.ForRead);
                bounds.AddExtents(firstCurve.GeometricExtents);
                foreach (var id in clusterIds.Skip(1))
                {
                    var curve = (Curve)tr.GetObject(id, OpenMode.ForRead);
                    bounds.AddExtents(curve.GeometricExtents);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { /* ignore errors */ }
            return bounds;
        }

        #endregion

        #region Recognition Logic

        private string RecognizeCluster(Database db, List<ObjectId> clusterIds)
        {
            List<NormalizedSegment> normalizedSegments = NormalizeCluster(db, clusterIds);
            if (!normalizedSegments.Any()) return null;

            string bestMatch = null;
            double bestScore = double.MaxValue;

            foreach (var template in _characterTemplates)
            {
                double score = Compare(normalizedSegments, template.Segments);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestMatch = template.Character;
                }
            }

            if (bestScore < RecognitionThreshold)
            {
                return bestMatch;
            }

            return null;
        }

        private List<NormalizedSegment> NormalizeCluster(Database db, List<ObjectId> clusterIds)
        {
            if (!clusterIds.Any()) return new List<NormalizedSegment>();

            var normalizedSegments = new List<NormalizedSegment>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var clusterBounds = GetClusterBounds(tr, clusterIds);
                if (clusterBounds.MinPoint == clusterBounds.MaxPoint) return normalizedSegments;

                double minX = clusterBounds.MinPoint.X;
                double minY = clusterBounds.MinPoint.Y;
                double width = clusterBounds.MaxPoint.X - minX;
                double height = clusterBounds.MaxPoint.Y - minY;

                if (width < 1e-6 && height < 1e-6) return normalizedSegments;

                double scale = Math.Max(width, height);
                if (scale < 1e-6) return normalizedSegments;

                foreach (var id in clusterIds)
                {
                    var curve = (Curve)tr.GetObject(id, OpenMode.ForRead);
                    if (curve is Line line)
                    {
                        normalizedSegments.Add(NormalizeSegment(line.StartPoint, line.EndPoint, minX, minY, scale));
                    }
                    else if (curve is Polyline poly)
                    {
                        for (int i = 0; i < poly.NumberOfVertices; i++)
                        {
                            if (poly.GetSegmentType(i) == SegmentType.Line)
                            {
                                LineSegment3d seg = poly.GetLineSegmentAt(i);
                                normalizedSegments.Add(NormalizeSegment(seg.StartPoint, seg.EndPoint, minX, minY, scale));
                            }
                        }
                    }
                    else if (curve is Arc arc)
                    {
                        int numSegments = 12;
                        Point3d prevPoint = arc.StartPoint;
                        for (int i = 1; i <= numSegments; i++)
                        {
                            double param = (double)i / numSegments;
                            Point3d currentPoint = arc.GetPointAtParameter(arc.StartParam + param * (arc.EndParam - arc.StartParam));
                            normalizedSegments.Add(NormalizeSegment(prevPoint, currentPoint, minX, minY, scale));
                            prevPoint = currentPoint;
                        }
                    }
                    else if (curve is Circle circle)
                    {
                        int numSegments = 24;
                        Point3d prevPoint = circle.GetPointAtParameter(circle.StartParam);
                        for (int i = 1; i <= numSegments; i++)
                        {
                            double param = circle.StartParam + ((double)i / numSegments) * (circle.EndParam - circle.StartParam);
                            Point3d currentPoint = circle.GetPointAtParameter(param);
                            normalizedSegments.Add(NormalizeSegment(prevPoint, currentPoint, minX, minY, scale));
                            prevPoint = currentPoint;
                        }
                    }
                    else if (curve is Spline spline)
                    {
                        if (spline.NumControlPoints > 1)
                        {
                            int numSegments = 20;
                            Point3d prevPoint = spline.StartPoint;
                            for (int i = 1; i <= numSegments; i++)
                            {
                                double param = spline.StartParam + ((double)i / numSegments) * (spline.EndParam - spline.StartParam);
                                Point3d currentPoint = spline.GetPointAtParameter(param);
                                normalizedSegments.Add(NormalizeSegment(prevPoint, currentPoint, minX, minY, scale));
                                prevPoint = currentPoint;
                            }
                        }
                    }
                }
                tr.Commit();
            }
            return normalizedSegments;
        }

        private NormalizedSegment NormalizeSegment(Point3d start, Point3d end, double minX, double minY, double scale)
        {
            var p1 = new Point2d((start.X - minX) / scale, (start.Y - minY) / scale);
            var p2 = new Point2d((end.X - minX) / scale, (end.Y - minY) / scale);
            return new NormalizedSegment(p1, p2);
        }

        private double Compare(List<NormalizedSegment> segmentsA, List<NormalizedSegment> segmentsB)
        {
            if (!segmentsA.Any() || !segmentsB.Any())
            {
                return double.MaxValue;
            }

            double totalDistance = 0;
            totalDistance += segmentsA.Sum(segA => segmentsB.Min(segB => SegmentDistance(segA, segB)));
            totalDistance += segmentsB.Sum(segB => segmentsA.Min(segA => SegmentDistance(segB, segA)));

            // Normalize the score by the total number of segments to make it comparable across different shapes
            return totalDistance / (segmentsA.Count + segmentsB.Count);
        }

        private double SegmentDistance(NormalizedSegment segA, NormalizedSegment segB)
        {
            double dist1 = (segA.Start - segB.Start).Length + (segA.End - segB.End).Length;
            double dist2 = (segA.Start - segB.End).Length + (segA.End - segB.Start).Length;
            return Math.Min(dist1, dist2) / 2.0;
        }

        #endregion

        #region Clustering and Fetching

        private List<List<ObjectId>> GroupCurves(Database db, List<ObjectId> allCurveIds)
        {
            var clusters = new List<List<ObjectId>>();
            var remainingIds = new List<ObjectId>(allCurveIds);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                while (remainingIds.Any())
                {
                    var currentCluster = new List<ObjectId>();
                    var queue = new Queue<ObjectId>();

                    var seedId = remainingIds[0];
                    queue.Enqueue(seedId);
                    currentCluster.Add(seedId);
                    remainingIds.RemoveAt(0);

                    while (queue.Any())
                    {
                        var idToProcess = queue.Dequeue();
                        var curveToProcess = (Curve)tr.GetObject(idToProcess, OpenMode.ForRead);
                        Extents3d bounds;
                        try { bounds = curveToProcess.GeometricExtents; } catch { continue; }

                        double tolerance = (bounds.MaxPoint.Y - bounds.MinPoint.Y) * 1.5;
                        if (tolerance < 1e-3) tolerance = (bounds.MaxPoint.X - bounds.MinPoint.X) * 1.5;
                        if (tolerance < 1e-3) tolerance = 1.0;

                        var expandedBounds = new Extents3d(
                            bounds.MinPoint - new Vector3d(tolerance, tolerance, 0),
                            bounds.MaxPoint + new Vector3d(tolerance, tolerance, 0)
                        );

                        for (int i = remainingIds.Count - 1; i >= 0; i--)
                        {
                            var otherId = remainingIds[i];
                            var otherCurve = (Curve)tr.GetObject(otherId, OpenMode.ForRead);
                            Extents3d otherBounds;
                            try { otherBounds = otherCurve.GeometricExtents; } catch { continue; }

                            if (BoundsOverlap(expandedBounds, otherBounds))
                            {
                                queue.Enqueue(otherId);
                                currentCluster.Add(otherId);
                                remainingIds.RemoveAt(i);
                            }
                        }
                    }
                    clusters.Add(currentCluster);
                }
                tr.Commit();
            }
            return clusters;
        }

        private bool BoundsOverlap(Extents3d b1, Extents3d b2)
        {
            return !(b1.MaxPoint.X < b2.MinPoint.X || b1.MinPoint.X > b2.MaxPoint.X ||
                     b1.MaxPoint.Y < b2.MinPoint.Y || b1.MinPoint.Y > b2.MaxPoint.Y);
        }

        private List<ObjectId> GetAllCurveIds(Database db)
        {
            var ids = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Curve))))
                    {
                        ids.Add(id);
                    }
                }
                tr.Commit();
            }
            return ids;
        }

        #endregion

        #region Character Templates

        private void InitializeTemplates()
        {
            if (_characterTemplates != null) return;

            _characterTemplates = new List<CharacterTemplate>();

            // Basic Cyrillic letters
            _characterTemplates.Add(new CharacterTemplate("А", new List<NormalizedSegment>
            {
                new NormalizedSegment(new Point2d(0, 0), new Point2d(0.5, 1)),
                new NormalizedSegment(new Point2d(0.5, 1), new Point2d(1, 0)),
                new NormalizedSegment(new Point2d(0.25, 0.5), new Point2d(0.75, 0.5))
            }));
            _characterTemplates.Add(new CharacterTemplate("Г", new List<NormalizedSegment>
            {
                new NormalizedSegment(new Point2d(0, 0), new Point2d(0, 1)),
                new NormalizedSegment(new Point2d(0, 1), new Point2d(1, 1))
            }));
            _characterTemplates.Add(new CharacterTemplate("И", new List<NormalizedSegment>
            {
                new NormalizedSegment(new Point2d(0, 0), new Point2d(0, 1)),
                new NormalizedSegment(new Point2d(1, 0), new Point2d(1, 1)),
                new NormalizedSegment(new Point2d(0, 1), new Point2d(1, 0))
            }));
            _characterTemplates.Add(new CharacterTemplate("П", new List<NormalizedSegment>
            {
                new NormalizedSegment(new Point2d(0, 0), new Point2d(0, 1)),
                new NormalizedSegment(new Point2d(1, 0), new Point2d(1, 1)),
                new NormalizedSegment(new Point2d(0, 1), new Point2d(1, 1))
            }));
            _characterTemplates.Add(new CharacterTemplate("Р", new List<NormalizedSegment>
            {
                new NormalizedSegment(new Point2d(0, 0), new Point2d(0, 1)),
                new NormalizedSegment(new Point2d(0, 1), new Point2d(0.8, 1)),
                new NormalizedSegment(new Point2d(0.8, 1), new Point2d(0.8, 0.5)),
                new NormalizedSegment(new Point2d(0.8, 0.5), new Point2d(0, 0.5))
            }));
            _characterTemplates.Add(new CharacterTemplate("Т", new List<NormalizedSegment>
            {
                new NormalizedSegment(new Point2d(0.5, 0), new Point2d(0.5, 1)),
                new NormalizedSegment(new Point2d(0, 1), new Point2d(1, 1))
            }));

            // Template for 'О' using a tessellated circle
            var oSegments = new List<NormalizedSegment>();
            int oNumSegments = 24;
            Point2d center = new Point2d(0.5, 0.5);
            double radius = 0.5;
            Point2d prevPoint = new Point2d(center.X + radius, center.Y);
            for (int i = 1; i <= oNumSegments; i++)
            {
                double angle = 2 * Math.PI * i / oNumSegments;
                Point2d currentPoint = new Point2d(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
                oSegments.Add(new NormalizedSegment(prevPoint, currentPoint));
                prevPoint = currentPoint;
            }
            _characterTemplates.Add(new CharacterTemplate("О", oSegments));

            // Template for 'C' using a tessellated arc
            var cSegments = new List<NormalizedSegment>();
            int cNumSegments = 20;
            double startAngle = 0.2 * Math.PI;
            double endAngle = 1.8 * Math.PI;
            Point2d cPrevPoint = new Point2d(center.X + radius * Math.Cos(startAngle), center.Y + radius * Math.Sin(startAngle));
            for (int i = 1; i <= cNumSegments; i++)
            {
                double angle = startAngle + (endAngle - startAngle) * i / cNumSegments;
                Point2d currentPoint = new Point2d(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
                cSegments.Add(new NormalizedSegment(cPrevPoint, currentPoint));
                cPrevPoint = currentPoint;
            }
            _characterTemplates.Add(new CharacterTemplate("С", cSegments));
        }
        #endregion
    }
}
