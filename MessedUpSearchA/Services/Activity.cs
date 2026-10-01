using System;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Services;

/// <summary>Запись в журнал событий. Сбой записи не мешает основному действию.</summary>
public static class Activity
{
    public static void Log(string kind, int? beatId = null, int? artistId = null, string value = "")
    {
        try
        {
            using var db = new AppDbContext();
            db.ActivityEvents.Add(new ActivityEvent
            {
                Kind = kind,
                At = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                BeatId = beatId,
                ArtistId = artistId,
                Value = value
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            AppLog.Write($"журнал событий ({kind}): {ex.Message}");
        }
    }
}
