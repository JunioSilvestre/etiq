using LabelPrinter.Core.Interfaces;
using LabelPrinter.Persistence.Context;
using LabelPrinter.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabelPrinter.Persistence;

/// <summary>
/// Extensões de registro de dependências para a camada de persistência.
/// </summary>
public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<LabelPrinterDbContext>(options =>
        {
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            });

            // Em desenvolvimento, logar queries SQL
#if DEBUG
            options.EnableSensitiveDataLogging(false);
            options.EnableDetailedErrors(true);
#endif
        });

        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IPrinterProfileRepository, PrinterProfileRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();

        return services;
    }

    /// <summary>
    /// Aplica as migrations do banco de dados ao iniciar a aplicação.
    /// </summary>
    public static async Task EnsureDatabaseMigratedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LabelPrinterDbContext>();
        await context.Database.MigrateAsync();
    }
}
