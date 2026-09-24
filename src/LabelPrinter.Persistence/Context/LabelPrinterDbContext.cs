using LabelPrinter.Core.Entities;
using LabelPrinter.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace LabelPrinter.Persistence.Context;

/// <summary>
/// Contexto principal do banco de dados SQLite.
/// Contém todas as entidades do sistema de impressão de etiquetas.
/// </summary>
public class LabelPrinterDbContext : DbContext
{
    public LabelPrinterDbContext(DbContextOptions<LabelPrinterDbContext> options)
        : base(options) { }

    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<PrinterProfile> PrinterProfiles => Set<PrinterProfile>();
    public DbSet<PrinterRoutingRule> PrinterRoutingRules => Set<PrinterRoutingRule>();
    public DbSet<DiscoveredPrinter> DiscoveredPrinters => Set<DiscoveredPrinter>();
    public DbSet<PrintAttempt> PrintAttempts => Set<PrintAttempt>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ============================
        // PrintJob
        // ============================
        modelBuilder.Entity<PrintJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.OriginalFilePath).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.OriginalHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.DocumentHash).HasMaxLength(64);
            entity.Property(e => e.LabelHash).HasMaxLength(64);
            entity.Property(e => e.PdfPath).HasMaxLength(1000);
            entity.Property(e => e.ArchivePath).HasMaxLength(1000);
            entity.Property(e => e.ExtractedPath).HasMaxLength(1000);
            entity.Property(e => e.OrderReference).HasMaxLength(200);
            entity.Property(e => e.TrackingNumber).HasMaxLength(200);
            entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
            entity.Property(e => e.DetectedEncoding).HasMaxLength(100);
            entity.Property(e => e.MarketplaceRaw).HasMaxLength(100);

            // Enum stored as string for readability
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.Marketplace)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.LabelSize)
                .HasConversion<string>()
                .HasMaxLength(50);

            // Indexes
            entity.HasIndex(e => e.OriginalHash).HasDatabaseName("IX_PrintJobs_OriginalHash");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_PrintJobs_Status");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("IX_PrintJobs_CreatedAt");
            entity.HasIndex(e => e.Marketplace).HasDatabaseName("IX_PrintJobs_Marketplace");

            // Relationships
            entity.HasOne(e => e.PrinterProfile)
                .WithMany(p => p.Jobs)
                .HasForeignKey(e => e.PrinterProfileId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Attempts)
                .WithOne(a => a.Job)
                .HasForeignKey(a => a.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.AuditEvents)
                .WithOne(a => a.Job)
                .HasForeignKey(a => a.JobId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================
        // PrinterProfile
        // ============================
        modelBuilder.Entity<PrinterProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.WindowsPrinterName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.DriverName).HasMaxLength(500);
            entity.Property(e => e.PortName).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(1000);

            entity.Property(e => e.Protocol)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.HasIndex(e => e.WindowsPrinterName).HasDatabaseName("IX_PrinterProfiles_WindowsPrinterName");
            entity.HasIndex(e => e.Enabled).HasDatabaseName("IX_PrinterProfiles_Enabled");
        });

        // ============================
        // PrinterRoutingRule
        // ============================
        modelBuilder.Entity<PrinterRoutingRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.Marketplace)
                .HasConversion<string?>()
                .HasMaxLength(50);
            entity.Property(e => e.LabelSize)
                .HasConversion<string?>()
                .HasMaxLength(50);

            entity.HasOne(e => e.PrinterProfile)
                .WithMany(p => p.RoutingRules)
                .HasForeignKey(e => e.PrinterProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================
        // DiscoveredPrinter
        // ============================
        modelBuilder.Entity<DiscoveredPrinter>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.WindowsName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.DriverName).HasMaxLength(500);
            entity.Property(e => e.PortName).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(200);

            entity.HasIndex(e => e.WindowsName).IsUnique().HasDatabaseName("IX_DiscoveredPrinters_WindowsName");
        });

        // ============================
        // PrintAttempt
        // ============================
        modelBuilder.Entity<PrintAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
            entity.Property(e => e.PrinterName).HasMaxLength(500);

            entity.Property(e => e.Result)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.HasIndex(e => e.JobId).HasDatabaseName("IX_PrintAttempts_JobId");
        });

        // ============================
        // AuditEvent
        // ============================
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Description).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Metadata).HasMaxLength(8000);

            entity.Property(e => e.EventType)
                .HasConversion<string>()
                .HasMaxLength(100);

            entity.HasIndex(e => e.JobId).HasDatabaseName("IX_AuditEvents_JobId");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("IX_AuditEvents_CreatedAt");
            entity.HasIndex(e => e.EventType).HasDatabaseName("IX_AuditEvents_EventType");
        });

        // ============================
        // AppSetting
        // ============================
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Value).HasMaxLength(4000);
        });
    }
}
