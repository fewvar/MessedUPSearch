namespace MessedUpSearchA.Models;

public class SentBeatsLog
{
    public int Id { get; set; }

    public int ArtistId { get; set; }
    public Artist? Artist { get; set; }

    public int BeatId { get; set; }
    public Beat? Beat { get; set; }

    public string AssignedAt { get; set; } = string.Empty;
    public bool IsSent { get; set; }
    public string SentAt { get; set; } = string.Empty;
}
