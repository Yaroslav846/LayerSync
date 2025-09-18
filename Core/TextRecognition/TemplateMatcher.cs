using Autodesk.AutoCAD.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LayerSync.Core.TextRecognition
{
    /// <summary>
    /// Manages a library of character templates and finds the best match for a given feature vector.
    /// </summary>
    public class TemplateMatcher
    {
        private readonly List<CharacterTemplate> _templates = new List<CharacterTemplate>();
        private readonly int _gridWidth;
        private readonly int _gridHeight;

        public TemplateMatcher(int gridWidth = 8, int gridHeight = 10)
        {
            _gridWidth = gridWidth;
            _gridHeight = gridHeight;
            LoadTemplates();
        }

        public char Match(bool[] features, out double bestScore)
        {
            bestScore = double.MaxValue;
            char bestMatch = '?';

            if (features.All(f => !f))
            {
                // If features are all false (empty character), it's likely a space or noise.
                return ' ';
            }

            foreach (var template in _templates)
            {
                double distance = HammingDistance(features, template.FeatureVector);
                if (distance < bestScore)
                {
                    bestScore = distance;
                    bestMatch = template.Character;
                }
            }

            // Normalize the score to be a percentage of the total number of features.
            double mismatchPercentage = bestScore / features.Length;

            // If the best match is still very poor (e.g., >35% mismatch), it's probably not a valid character.
            if (mismatchPercentage > 0.35)
            {
                return '?';
            }

            return bestMatch;
        }

        private int HammingDistance(bool[] a, bool[] b)
        {
            int distance = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    distance++;
                }
            }
            return distance;
        }

        private void LoadTemplates()
        {
            // This is the manual part. Each 8x10 grid is defined as a flat boolean array.
            // I'll define a few characters from "Рабочие" to start.

            // Template for 'Р'
            _templates.Add(new CharacterTemplate('Р', new bool[]
            {
                false,true,true,true,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,true,true,true,true,false,false,false,
                false,true,false,false,false,false,false,false,
                false,true,false,false,false,false,false,false,
                false,true,false,false,false,false,false,false,
                false,true,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
            }));

            // Template for 'а'
            _templates.Add(new CharacterTemplate('а', new bool[]
            {
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,true,true,true,false,false,false,
                false,true,false,false,true,true,false,false,
                false,true,false,false,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,false,true,true,true,true,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
            }));

            // Template for 'б'
            _templates.Add(new CharacterTemplate('б', new bool[]
            {
                false,true,true,true,false,false,false,false,
                false,true,false,false,false,false,false,false,
                false,true,false,false,false,false,false,false,
                false,true,true,true,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,false,true,true,false,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
            }));

            // Template for 'о'
            _templates.Add(new CharacterTemplate('о', new bool[]
            {
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,true,true,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,false,true,true,true,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
            }));

            // Template for 'ч'
            _templates.Add(new CharacterTemplate('ч', new bool[]
            {
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,true,false,false,false,false,
                false,false,true,false,true,false,false,false,
                false,true,false,false,true,false,false,false,
                false,true,true,true,true,true,false,false,
                false,false,false,false,true,false,false,false,
                false,false,false,false,false,false,false,false,
                false,false,false,false,false,false,false,false,
            }));
        }

        /// <summary>
        /// A helper method to print a feature vector to the AutoCAD command line for debugging.
        /// </summary>
        public void Debug_PrintFeatures(bool[] features)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("\n--- Feature Vector ---");
            for (int y = 0; y < _gridHeight; y++)
            {
                for (int x = 0; x < _gridWidth; x++)
                {
                    sb.Append(features[y * _gridWidth + x] ? "#" : ".");
                }
                sb.AppendLine();
            }
            sb.AppendLine("----------------------");
            doc.Editor.WriteMessage(sb.ToString());
        }
    }
}
