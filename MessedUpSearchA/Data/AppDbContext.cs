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
    public DbSet<TrackEmbedding> TrackEmbeddings => Set<TrackEmbedding>();
    public DbSet<MailTemplate> MailTemplates => Set<MailTemplate>();
    public DbSet<OutgoingMail> OutgoingMails => Set<OutgoingMail>();
    public DbSet<IncomingReply> IncomingReplies => Set<IncomingReply>();

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

        // Удалили артиста — строка в выдаче остаётся с именем, но без ссылки на карточку.
        b.Entity<BeatSimilarity>()
            .HasOne<Artist>()
            .WithMany()
            .HasForeignKey(s => s.ArtistId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<TrackEmbedding>()
            .HasOne(e => e.ArtistTrack)
            .WithMany()
            .HasForeignKey(e => e.ArtistTrackId)
            .OnDelete(DeleteBehavior.Cascade);

        // Один вектор на трек и модель: пересчёт заменяет, а не копит дубли.
        b.Entity<TrackEmbedding>()
            .HasIndex(e => new { e.ArtistTrackId, e.ModelVersion })
            .IsUnique();

        // Журнал писем переживает удаление артиста, бита и шаблона — для статистики.
        b.Entity<OutgoingMail>()
            .HasOne<Artist>().WithMany().HasForeignKey(m => m.ArtistId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<OutgoingMail>()
            .HasOne<Beat>().WithMany().HasForeignKey(m => m.BeatId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<OutgoingMail>()
            .HasOne<MailTemplate>().WithMany().HasForeignKey(m => m.TemplateId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<OutgoingMail>().HasIndex(m => m.ArtistId);
        b.Entity<OutgoingMail>().HasIndex(m => m.MessageId);

        b.Entity<IncomingReply>()
            .HasOne<Artist>().WithMany().HasForeignKey(r => r.ArtistId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<IncomingReply>()
            .HasOne<OutgoingMail>().WithMany().HasForeignKey(r => r.OutgoingMailId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<IncomingReply>().HasIndex(r => r.MessageId);
        b.Entity<IncomingReply>().HasIndex(r => r.ArtistId);
    }
}
