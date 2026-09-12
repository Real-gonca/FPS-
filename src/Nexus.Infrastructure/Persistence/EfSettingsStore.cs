using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Persistence;

public sealed class EfSettingsStore : ScopedDbAccess, ISettingsService
{
    public EfSettingsStore(IServiceScopeFactory scopes, ILogger<EfSettingsStore> log)
        : base(scopes, log)
    {
    }

    public Task<AppSettings> LoadAsync(CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            var row = await db.Settings.FindAsync(new object[] { 1 }, ct);
            return row is null
                ? new AppSettings()
                : new AppSettings
                {
                    AdvancedMode = row.AdvancedMode,
                    ChartWindowHours = row.ChartWindowHours,
                    NotificationAutoDismissSeconds = row.NotificationAutoDismissSeconds,
                };
        }, ct);

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            var row = await db.Settings.FindAsync(new object[] { 1 }, ct);
            if (row is null)
            {
                db.Settings.Add(new SettingsRow
                {
                    AdvancedMode = settings.AdvancedMode,
                    ChartWindowHours = settings.ChartWindowHours,
                    NotificationAutoDismissSeconds = settings.NotificationAutoDismissSeconds,
                });
            }
            else
            {
                row.AdvancedMode = settings.AdvancedMode;
                row.ChartWindowHours = settings.ChartWindowHours;
                row.NotificationAutoDismissSeconds = settings.NotificationAutoDismissSeconds;
            }
            await db.SaveChangesAsync(ct);
        }, ct);
}
