using LabelPrinter.Core.Entities;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace LabelPrinter.Persistence.Repositories;

/// <summary>
/// Repositório de jobs de impressão.
/// </summary>
public class JobRepository : IJobRepository
{
    private readonly LabelPrinterDbContext _context;

    public JobRepository(LabelPrinterDbContext context)
    {
        _context = context;
    }

    public async Task<PrintJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.PrintJobs
            .Include(j => j.Attempts)
            .Include(j => j.PrinterProfile)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<PrintJob?> GetByHashAsync(string hash, CancellationToken cancellationToken = default) =>
        await _context.PrintJobs
            .Where(j => j.OriginalHash == hash && !j.IsReprint)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> GetByStatusAsync(
        JobStatus status,
        CancellationToken cancellationToken = default) =>
        await _context.PrintJobs
            .Where(j => j.Status == status)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> GetActiveJobsAsync(
        CancellationToken cancellationToken = default) =>
        await _context.PrintJobs
            .Where(j => j.Status == JobStatus.Queued
                     || j.Status == JobStatus.Processing
                     || j.Status == JobStatus.Rendered
                     || j.Status == JobStatus.PdfGenerated
                     || j.Status == JobStatus.WaitingForPrinter
                     || j.Status == JobStatus.Printing
                     || j.Status == JobStatus.Retrying)
            .Include(j => j.PrinterProfile)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        await _context.PrintJobs.AddAsync(job, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        job.UpdatedAt = DateTime.UtcNow;
        _context.PrintJobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Repositório de perfis de impressora.
/// </summary>
public class PrinterProfileRepository : IPrinterProfileRepository
{
    private readonly LabelPrinterDbContext _context;

    public PrinterProfileRepository(LabelPrinterDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PrinterProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.PrinterProfiles
            .Include(p => p.RoutingRules)
            .OrderBy(p => p.Priority)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PrinterProfile>> GetEnabledAsync(CancellationToken cancellationToken = default) =>
        await _context.PrinterProfiles
            .Where(p => p.Enabled)
            .Include(p => p.RoutingRules)
            .OrderBy(p => p.Priority)
            .ToListAsync(cancellationToken);

    public async Task<PrinterProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.PrinterProfiles
            .Include(p => p.RoutingRules)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task AddOrUpdateAsync(PrinterProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = await _context.PrinterProfiles.FindAsync([profile.Id], cancellationToken);
        if (existing == null)
        {
            await _context.PrinterProfiles.AddAsync(profile, cancellationToken);
        }
        else
        {
            existing.Name = profile.Name;
            existing.WindowsPrinterName = profile.WindowsPrinterName;
            existing.DriverName = profile.DriverName;
            existing.PortName = profile.PortName;
            existing.Protocol = profile.Protocol;
            existing.Dpi = profile.Dpi;
            existing.DefaultLabelWidthMm = profile.DefaultLabelWidthMm;
            existing.DefaultLabelHeightMm = profile.DefaultLabelHeightMm;
            existing.Enabled = profile.Enabled;
            existing.Priority = profile.Priority;
            existing.MaxRetries = profile.MaxRetries;
            existing.RetryDelaySeconds = profile.RetryDelaySeconds;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Repositório de auditoria.
/// </summary>
public class AuditRepository : IAuditRepository
{
    private readonly LabelPrinterDbContext _context;

    public AuditRepository(LabelPrinterDbContext context)
    {
        _context = context;
    }

    public async Task AddEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await _context.AuditEvents.AddAsync(auditEvent, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddPrintAttemptAsync(PrintAttempt attempt, CancellationToken cancellationToken = default)
    {
        await _context.PrintAttempts.AddAsync(attempt, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
