namespace LayerSync.Core.TextRecognition
{
    /// <summary>
    /// A simple data class to store a known character and its pre-computed feature vector.
    /// </summary>
    public class CharacterTemplate
    {
        public char Character { get; }
        public bool[] FeatureVector { get; }

        public CharacterTemplate(char character, bool[] featureVector)
        {
            Character = character;
            FeatureVector = featureVector;
        }
    }
}
