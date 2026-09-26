using MessedUpSearchA.Models;

namespace MessedUpSearchA.ViewModels;

/// <summary>Строка трека в карточке артиста.</summary>
public class ArtistTrackItem
{
    public ArtistTrackItem(ArtistTrack track) => Track = track;

    public ArtistTrack Track { get; }

    public string Title => Track.Title;

    public string Meta => Track.PlayCount > 0
        ? $"{Track.SourcePlatform} · {Track.PlayCount:N0}"
        : Track.SourcePlatform;
}
