namespace MessedUpSearchA.Models;

public class Artist
{
    public int Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string ScLink { get; set; } = string.Empty;
    public string AvatarPath { get; set; } = string.Empty;
    public string IgLink { get; set; } = string.Empty;
    public string SpotifyLink { get; set; } = string.Empty;
    public string LinktreeLink { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int TotalPlays { get; set; }
    public string LastTrackDate { get; set; } = string.Empty;
    public string AiGenreTags { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public bool IsRedFlagged { get; set; }
    public string CrmStatus { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string LastParsed { get; set; } = string.Empty;
}
