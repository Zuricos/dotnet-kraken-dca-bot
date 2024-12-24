using System.Reflection;
using Kbot.Common.Api;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.MailService.Database;
using Kbot.MailService.Options;
using Microsoft.EntityFrameworkCore;

namespace Kbot.MailService.Utility;

public static class ServiceCollectionExtension
{
    public static IServiceCollection Setup(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Secrets>(configuration.GetSection(nameof(Secrets)));
        services.Configure<MailSecrets>(configuration.GetSection(nameof(MailSecrets)));
        services.Configure<MailOptions>(configuration.GetSection(nameof(MailOptions)));

        services.AddDbContextFactory<KrakenDbContext>(options =>
        {
            options.UseNpgsql(connectionString: configuration.GetConnectionString("Kraken"));
        });

        services.AddTransient<OrderService>();
        services.AddTransient<KrakenApi>();
        services.AddTransient<KrakenClient>();
        services.AddSingleton<MailSenderService>();
        services.AddSingleton<MigrationService>();

        services.AddHostedService<OptionsCheckerService<Secrets>>();
        services.AddHostedService<OptionsCheckerService<MailSecrets>>();
        services.AddHostedService<Worker>();
        return services;
    }

    public static IConfigurationManager Setup(this IConfigurationManager configManager, string environment)
    {
        configManager
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddJsonFile("/run/secrets/mail-secrets", optional: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .AddUserSecrets<Secrets>()
            .AddUserSecrets<MailSecrets>()
            .AddEnvironmentVariables()
            .Build();

        return configManager;
    }
}
