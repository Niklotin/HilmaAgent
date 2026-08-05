using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Ingestion;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;
using HilmaAgent.Core.Providers;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HilmaAgent.Infrastructure.Persistence;

public class HilmaDbContext(DbContextOptions<HilmaDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeChunk> NoticeChunks => Set<NoticeChunk>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<FitAssessment> FitAssessments => Set<FitAssessment>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<IngestionCheckpoint> IngestionCheckpoints => Set<IngestionCheckpoint>();
    public DbSet<ProviderCredential> ProviderCredentials => Set<ProviderCredential>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    /// <summary>
    /// The data-protection key ring, kept in Postgres rather than on a container filesystem.
    /// </summary>
    /// <remarks>
    /// Stored API keys are encrypted with these. On a container filesystem they would be regenerated
    /// on every rebuild, and every stored credential would become permanently undecryptable — a
    /// failure that surfaces later as an authentication error from the provider and sends you looking
    /// in entirely the wrong place. Postgres is already the durable volume, so they live there.
    /// </remarks>
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys
        => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

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

        modelBuilder.Entity<NoticeChunk>(chunk =>
        {
            chunk.HasKey(c => c.Id);
            chunk.Property(c => c.NoticeId).HasMaxLength(128);
            chunk.Property(c => c.Section).HasMaxLength(32);
            chunk.Property(c => c.LotId).HasMaxLength(64);
            chunk.Property(c => c.EmbeddingModel).HasMaxLength(128);

            chunk.HasOne(c => c.Notice)
                .WithMany()
                .HasForeignKey(c => c.NoticeId)
                // Chunks are derived data; a deleted notice must not leave them behind.
                .OnDelete(DeleteBehavior.Cascade);

            chunk.HasIndex(c => c.NoticeId);
            chunk.HasIndex(c => c.EmbeddingModel);
        });

        modelBuilder.Entity<CompanyProfile>(profile =>
        {
            profile.HasKey(p => p.Id);
            profile.Property(p => p.Name).HasMaxLength(256);
            profile.Property(p => p.Technologies).HasColumnType("text[]");
            profile.Property(p => p.ReferenceProjects).HasColumnType("text[]");
            profile.Property(p => p.PreferredCpvCodes).HasColumnType("text[]");
            profile.Property(p => p.Regions).HasColumnType("text[]");
        });

        modelBuilder.Entity<FitAssessment>(assessment =>
        {
            assessment.HasKey(a => a.Id);
            assessment.Property(a => a.NoticeId).HasMaxLength(128);
            assessment.Property(a => a.ScoreRecommendation).HasMaxLength(16);
            assessment.Property(a => a.ModelRecommendation).HasMaxLength(16);
            assessment.Property(a => a.ModelId).HasMaxLength(128);
            assessment.Property(a => a.ScoreBreakdownJson).HasColumnType("jsonb");

            // Citations are an owned collection serialised into the row: they are meaningless apart
            // from their assessment and are never queried independently.
            assessment.OwnsMany(a => a.Citations, citations => citations.ToJson());

            assessment.HasOne(a => a.Notice).WithMany().HasForeignKey(a => a.NoticeId).OnDelete(DeleteBehavior.Cascade);
            assessment.HasOne(a => a.Profile).WithMany().HasForeignKey(a => a.ProfileId).OnDelete(DeleteBehavior.Cascade);

            assessment.HasIndex(a => a.NoticeId);
            assessment.HasIndex(a => a.CreatedAt);
        });

        modelBuilder.Entity<ApprovalDecision>(decision =>
        {
            decision.HasKey(d => d.Id);
            decision.Property(d => d.Decision).HasMaxLength(16);
            decision.Property(d => d.EditedRecommendation).HasMaxLength(16);
            decision.Property(d => d.ReviewedBy).HasMaxLength(128);

            decision.HasOne(d => d.Assessment)
                .WithMany()
                .HasForeignKey(d => d.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);

            decision.HasIndex(d => d.AssessmentId);
            decision.HasIndex(d => d.DecidedAt);
        });

        modelBuilder.Entity<ProviderCredential>(credential =>
        {
            credential.HasKey(c => c.Provider);
            credential.Property(c => c.Provider).HasMaxLength(64);
            credential.Property(c => c.KeyHint).HasMaxLength(8);
            credential.Property(c => c.BaseUrl).HasMaxLength(512);
            credential.Property(c => c.Model).HasMaxLength(128);
            credential.Property(c => c.UpdatedBy).HasMaxLength(128);
        });

        modelBuilder.Entity<AppSetting>(setting =>
        {
            setting.HasKey(s => s.Key);
            setting.Property(s => s.Key).HasMaxLength(128);
            setting.Property(s => s.Value).HasMaxLength(512);
        });

        modelBuilder.Entity<IngestionCheckpoint>(checkpoint =>
        {
            checkpoint.HasKey(c => c.Source);
            checkpoint.Property(c => c.Source).HasMaxLength(64);
        });
    }
}
