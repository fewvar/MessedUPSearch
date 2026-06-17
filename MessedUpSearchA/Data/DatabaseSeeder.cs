using System.Linq;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Data;

/// <summary>
/// Заполняет базу тестовыми данными при первом запуске (когда таблицы пусты).
/// Нужно, чтобы UI не выглядел пустым до того, как заработает парсер SoundCloud.
/// Идемпотентен: на повторных запусках ничего не дублирует.
/// </summary>
public static class DatabaseSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.Artists.Any())
        {
            db.Artists.AddRange(
                new Artist
                {
                    Nickname = "KXSHDAMI",
                    ScLink = "https://soundcloud.com/kxshdami",
                    AiGenreTags = "Rage, Plugg",
                    TotalPlays = 8120,
                    IgLink = "@kxshdami",
                    SpotifyLink = "23000",
                    Language = "ENG",
                    AvatarColor = "#E74C3C",
                    IsFavorite = true,
                    IsRedFlagged = false
                },
                new Artist
                {
                    Nickname = "NIGHTTEARS",
                    ScLink = "https://soundcloud.com/nighttears",
                    AiGenreTags = "Sad, Cloud",
                    TotalPlays = 6890,
                    IgLink = "@night.tears",
                    SpotifyLink = "9400",
                    Language = "RU",
                    AvatarColor = "#5DADE2",
                    IsFavorite = false,
                    IsRedFlagged = true
                });
        }

        if (!db.Beats.Any())
        {
            db.Beats.AddRange(
                new Beat
                {
                    BeatName = "VOODOO",
                    AiTags = "Rage",
                    Bpm = 145,
                    Key = "C minor",
                    Added = "2026-05-24",
                    Status = "POTENTIAL",
                    StatusColor = "#F1C40F"
                },
                new Beat
                {
                    BeatName = "LATE NIGHT",
                    AiTags = "Cloud, Sad",
                    Bpm = 120,
                    Key = "F# minor",
                    Added = "2026-05-20",
                    Status = "SOLD",
                    StatusColor = "#2ECC71"
                });
        }

        db.SaveChanges();
    }
}
