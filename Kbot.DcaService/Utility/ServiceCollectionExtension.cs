using System.Reflection;
using Kbot.Common.Api;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Options;

namespace Kbot.DcaService.Utility;

public static class ServiceCollectionExtension
{
    public static IServiceCollection Setup(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Secrets>(configuration.GetSection(nameof(Secrets)));
        services.Configure<OrderOptions>(configuration.GetSection(nameof(OrderOptions)));
        services.Configure<BalanceOptions>(configuration.GetSection(nameof(BalanceOptions)));
        services.Configure<WaitOptions>(configuration.GetSection(nameof(WaitOptions)));
        services.Configure<CultureOptions>(configuration.GetSection(nameof(CultureOptions)));

        services.AddTransient<KrakenApi>();
        services.AddTransient<KrakenClient>();
        services.AddTransient<TimeComputeService>();
        services.AddSingleton<HolidayService>();

        services.AddHostedService<OptionsCheckerService<Secrets>>();
        services.AddHostedService<OptionsCheckerService<OrderOptions>>();
        services.AddHostedService<OptionsCheckerService<BalanceOptions>>();
        services.AddHostedService<OptionsCheckerService<WaitOptions>>();
        services.AddHostedService<OptionsCheckerService<CultureOptions>>();

        services.AddHostedService(c => c.GetRequiredService<HolidayService>());
        services.AddHostedService<DcaWorker>();

        services.AddOptions();
        return services;
    }

    public static IConfigurationManager Setup(this IConfigurationManager configManager, string environment)
    {
        configManager
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddJsonFile("/run/secrets/dca-secrets", optional: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .AddUserSecrets<Secrets>()
            .AddEnvironmentVariables()
            .Build();

        return configManager;
    }
}
