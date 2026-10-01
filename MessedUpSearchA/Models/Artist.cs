namespace MessedUpSearchA.Models;

public class Artist
{
    public int Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? ScLink { get; set; }
    public string AvatarPath { get; set; } = string.Empty;
    public string AvatarColor { get; set; } = "#888888";
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

    // Контакты для рассылки (1.1). Заполняются руками или из подсказок по описанию профиля.
    public string Email { get; set; } = string.Empty;
    public string Telegram { get; set; } = string.Empty;
    public string OtherContact { get; set; } = string.Empty;

    /// <summary>Описание профиля с площадки — из него подсказываются контакты.</summary>
    public string Bio { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string LastParsed { get; set; } = string.Empty;

    public string SourcePlatform { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Когда искали артиста на Deezer ради превью. Пусто — ещё не искали.
    /// Ищем один раз: не нашёлся — значит, и завтра не найдётся.
    /// </summary>
    public string DeezerCheckedAt { get; set; } = string.Empty;
}
