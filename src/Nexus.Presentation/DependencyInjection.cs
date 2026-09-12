using Microsoft.Extensions.DependencyInjection;
using Nexus.Presentation.Notifications;
using Nexus.Presentation.ViewModels;
using Nexus.Presentation.Views;

namespace Nexus.Presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddNexusPresentation(this IServiceCollection services)
    {
        services.AddSingleton<NotificationCenter>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<QuickOptimizationViewModel>();
        services.AddSingleton<ServicesViewModel>();
        services.AddSingleton<PrivacyViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
