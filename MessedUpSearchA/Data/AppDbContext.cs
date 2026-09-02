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
    public DbSet<ArtistTrack> ArtistTracks => Set<ArtistTrack>();
    public DbSet<BeatSimilarity> BeatSimilarities => Set<BeatSimilarity>();

    public static string DbPath => AppPaths.DbFile;

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

        b.Entity<Artist>()
            .HasIndex(a => a.SourceUrl)
            .IsUnique()
            .HasFilter("\"SourceUrl\" IS NOT NULL");

        b.Entity<ArtistTrack>()
            .HasOne(t => t.Artist)
            .WithMany()
            .HasForeignKey(t => t.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ArtistTrack>()
            .HasIndex(t => t.Url)
            .IsUnique()
            .HasFilter("\"Url\" IS NOT NULL");

        b.Entity<BeatSimilarity>()
            .HasOne(s => s.Beat)
            .WithMany()
            .HasForeignKey(s => s.BeatId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ищем всегда по биту — «покажи, на кого похож вот этот».
        b.Entity<BeatSimilarity>()
            .HasIndex(s => s.BeatId);
    }
}
