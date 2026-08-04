using HilmaAgent.Core.Ingestion;
using HilmaAgent.Core.Notices;
using Microsoft.EntityFrameworkCore;

namespace HilmaAgent.Infrastructure.Persistence;

public class HilmaDbContext(DbContextOptions<HilmaDbContext> options) : DbContext(options)
{
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<IngestionCheckpoint> IngestionCheckpoints => Set<IngestionCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notice>(notice =>
        {
            notice.HasKey(n => n.Id);
            notice.Property(n => n.Id).HasMaxLength(128);
            notice.Property(n => n.NoticeType).HasMaxLength(128);
            notice.Property(n => n.Title).HasMaxLength(1024);
            notice.Property(n => n.BuyerName).HasMaxLength(512);
            notice.Property(n => n.BuyerOrganizationType).HasMaxLength(256);
            notice.Property(n => n.BuyerNationalRegistrationNumber).HasMaxLength(64);
            notice.Property(n => n.Currency).HasMaxLength(8);
            notice.Property(n => n.Language).HasMaxLength(8);
            notice.Property(n => n.CpvCodes).HasColumnType("text[]");
            notice.Property(n => n.Region).HasColumnType("text[]");
            notice.Property(n => n.RawPayload).HasColumnType("jsonb");
            notice.Property(n => n.Source).HasMaxLength(16);
            notice.HasIndex(n => n.Source);

            notice.HasIndex(n => n.PublicationDate);
            notice.HasIndex(n => n.SubmissionDeadline);
            notice.HasIndex(n => n.CpvCodes).HasMethod("gin");
            notice.HasIndex(n => n.Region).HasMethod("gin");
        });

        modelBuilder.Entity<IngestionCheckpoint>(checkpoint =>
        {
            checkpoint.HasKey(c => c.Source);
            checkpoint.Property(c => c.Source).HasMaxLength(64);
        });
    }
}
