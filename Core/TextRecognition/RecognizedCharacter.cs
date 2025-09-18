namespace LayerSync.Core.TextRecognition
{
    /// <summary>
    /// A data class that associates a recognized character with its original geometric cluster.
    /// This is used for the text reconstruction process.
    /// </summary>
    public class RecognizedCharacter
    {
        public char Char { get; }
        public CharacterCluster Cluster { get; }

        public RecognizedCharacter(char character, CharacterCluster cluster)
        {
            Char = character;
            Cluster = cluster;
        }
    }
}
