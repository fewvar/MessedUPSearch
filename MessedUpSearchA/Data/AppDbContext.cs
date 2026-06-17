using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Data;

/// <summary>
/// Контекст EF Core — описывает таблицы и подключение к SQLite.
/// Файл базы лежит в системной папке данных пользователя, не в папке проекта
/// (иначе затирался бы при пересборке).
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Beat> Beats => Set<Beat>();
    public DbSet<SentBeatsLog> SentBeatsLog => Set<SentBeatsLog>();

    /// <summary>Полный путь к app.db в папке данных приложения (кроссплатформенно).</summary>
    public static string DbPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MessedUpSearch");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "app.db");
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Дедупликация артистов по ссылке на профиль (ТЗ §2).
        b.Entity<Artist>()
            .HasIndex(a => a.ScLink)
            .IsUnique();

        // Связи лога отправок: при удалении артиста/бита — чистим записи лога.
        b.Entity<SentBeatsLog>()
            .HasOne(s => s.Artist)
            .WithMany()
            .HasForeignKey(s => s.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<SentBeatsLog>()
            .HasOne(s => s.Beat)
            .WithMany()
            .HasForeignKey(s => s.BeatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
