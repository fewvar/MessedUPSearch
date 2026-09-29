namespace MessedUpSearchA.Models;

/// <summary>
/// Вектор звучания одного трека артиста, посчитанный MERT по звуку из сети.
/// Сам звук не хранится — только этот вектор, из него запись не восстановить.
/// </summary>
public class TrackEmbedding
{
    public int Id { get; set; }

    public int ArtistTrackId { get; set; }
    public ArtistTrack? ArtistTrack { get; set; }

    /// <summary>
    /// 768 float подряд, сырой вектор — до вычитания центра. Центр можно
    /// поменять потом, не перекачивая треки заново.
    /// </summary>
    public byte[] Vector { get; set; } = [];

    /// <summary>"Full" — весь трек, "Preview" — 30-секундный кусок (Deezer).</summary>
    public string AudioKind { get; set; } = string.Empty;

    public double Seconds { get; set; }

    /// <summary>Какой моделью считали. Сменится модель — старые векторы несравнимы.</summary>
    public string ModelVersion { get; set; } = string.Empty;

    public string ComputedAt { get; set; } = string.Empty;
}
