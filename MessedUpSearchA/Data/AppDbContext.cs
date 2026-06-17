using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Data;

public class AppDbContext : DbContext
{
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Beat> Beats => Set<Beat>();
    public DbSet<SentBeatsLog> SentBeatsLog => Set<SentBeatsLog>();

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

        b.Entity<Artist>()
            .HasIndex(a => a.ScLink)
            .IsUnique()
            .HasFilter("\"ScLink\" IS NOT NULL");

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
