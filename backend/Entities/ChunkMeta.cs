namespace backend.Entities;

public class ChunkMeta
{
    public string ImageId { get; }
    public int TotalChunks { get; }

    public ChunkMeta(string imageId, int totalChunks)
    {
        ImageId = imageId;
        TotalChunks = totalChunks;
    }
}