using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LayerSync.Core.TextRecognition
{
    /// <summary>
    /// Reconstructs a final string from a list of recognized characters and their positions.
    /// </summary>
    public class TextReconstructor
    {
        /// <summary>
        /// Takes a list of recognized characters and arranges them into a formatted string with lines and spaces.
        /// </summary>
        public string Reconstruct(List<RecognizedCharacter> characters)
        {
            if (characters == null || characters.Count == 0) return "";

            // 1. Group characters into lines based on vertical overlap.
            var lines = new List<List<RecognizedCharacter>>();
            var sortedByY = characters.OrderBy(c => c.Cluster.BoundingBox.MinPoint.Y).ToList();

            while (sortedByY.Count > 0)
            {
                var currentLine = new List<RecognizedCharacter>();
                var firstCharInLine = sortedByY[0];
                currentLine.Add(firstCharInLine);
                sortedByY.RemoveAt(0);

                // Define the vertical center of the first character as the baseline for the line
                double lineCenterY = (firstCharInLine.Cluster.BoundingBox.MinPoint.Y + firstCharInLine.Cluster.BoundingBox.MaxPoint.Y) / 2.0;

                // A line's height is determined by its first character. This is a heuristic.
                double lineHeight = firstCharInLine.Cluster.BoundingBox.MaxPoint.Y - firstCharInLine.Cluster.BoundingBox.MinPoint.Y;
                if (lineHeight < 1e-6) lineHeight = 1.0; // Avoid zero height

                for (int i = sortedByY.Count - 1; i >= 0; i--)
                {
                    var otherChar = sortedByY[i];
                    var otherBox = otherChar.Cluster.BoundingBox;

                    // A character belongs to this line if its vertical center is "close" to the line's center.
                    // A robust heuristic is to check if the centers are within half a line height of each other.
                    double otherCenterY = (otherBox.MinPoint.Y + otherBox.MaxPoint.Y) / 2.0;
                    if (System.Math.Abs(lineCenterY - otherCenterY) < lineHeight * 0.75)
                    {
                        currentLine.Add(otherChar);
                        sortedByY.RemoveAt(i);
                    }
                }
                lines.Add(currentLine);
            }

            // 2. For each line, sort by X and insert spaces, then build the final string.
            var resultBuilder = new StringBuilder();
            // Sort lines by their average Y position to ensure correct order
            var sortedLines = lines.OrderBy(line => line.Average(c => c.Cluster.BoundingBox.MinPoint.Y));

            foreach (var line in sortedLines)
            {
                var sortedLine = line.OrderBy(c => c.Cluster.BoundingBox.MinPoint.X).ToList();

                if (sortedLine.Count == 0) continue;

                // Calculate average character width for this line to use as a space-detection heuristic.
                double averageCharWidth = sortedLine.Average(c => c.Cluster.BoundingBox.MaxPoint.X - c.Cluster.BoundingBox.MinPoint.X);
                // NOTE: The space threshold is a heuristic. 40% of the average character width is a reasonable guess.
                // This might need tuning for fonts that are very wide or very narrow.
                double spaceThreshold = averageCharWidth * 0.4;

                for (int i = 0; i < sortedLine.Count; i++)
                {
                    var character = sortedLine[i];
                    // Don't append the placeholder for empty characters, but let them influence spacing.
                    if (character.Char != ' ')
                    {
                        resultBuilder.Append(character.Char);
                    }

                    if (i < sortedLine.Count - 1)
                    {
                        var nextCharacter = sortedLine[i + 1];
                        var currentBox = character.Cluster.BoundingBox;
                        var nextBox = nextCharacter.Cluster.BoundingBox;
                        double gap = nextBox.MinPoint.X - currentBox.MaxPoint.X;

                        if (gap > spaceThreshold)
                        {
                            resultBuilder.Append(' ');
                        }
                    }
                }
                resultBuilder.AppendLine();
            }

            return resultBuilder.ToString().Trim();
        }
    }
}
