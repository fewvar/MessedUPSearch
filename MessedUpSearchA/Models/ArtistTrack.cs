namespace MessedUpSearchA.Models;

public class ArtistTrack
{
    public int Id { get; set; }

    public int ArtistId { get; set; }
    public Artist? Artist { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string SourcePlatform { get; set; } = string.Empty;

    public int PlayCount { get; set; }
    public string ReleasedAt { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;

    public bool IsDownloadable { get; set; }
    public string LocalFilePath { get; set; } = string.Empty;

    public string FetchedAt { get; set; } = string.Empty;
}
