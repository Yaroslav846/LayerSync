using Autodesk.AutoCAD.Geometry;

namespace LayerSync.Core
{
    public class RecognizedLine
    {
        public string Text { get; set; }
        public Point3d Position { get; set; }
        public double Height { get; set; }
    }
}
