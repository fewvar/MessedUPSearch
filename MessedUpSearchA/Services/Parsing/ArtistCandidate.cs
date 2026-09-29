using System.Collections.Generic;

namespace MessedUpSearchA.Services.Parsing;

public class ArtistCandidate
{
    public string Platform { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;

    public string AvatarUrl { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;

    public int Followers { get; init; }

    public int TotalPlays { get; set; }
    public string LastTrackDate { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;

    public string IgLink { get; set; } = string.Empty;
    public string SpotifyLink { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;

    public List<TrackInfo> Tracks { get; } = new();
}

public class TrackInfo
{
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;

    /// <summary>Id трека на площадке — по нему потом достаётся звук. Пусто, если площадка звук не отдаёт.</summary>
    public string SourceId { get; init; } = string.Empty;

    public int PlayCount { get; init; }
    public string ReleasedAt { get; init; } = string.Empty;
    public string Tags { get; init; } = string.Empty;
    public bool IsDownloadable { get; init; }
}
