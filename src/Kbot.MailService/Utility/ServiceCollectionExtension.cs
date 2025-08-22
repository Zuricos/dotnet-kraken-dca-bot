using System.Reflection;
using Kbot.Common.Api;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.MailService.Database;
using Kbot.MailService.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kbot.MailService.Utility;

public static class ServiceCollectionExtension
{
  public static IServiceCollection Setup(
    this IServiceCollection services,
    IConfiguration configuration
  )
  {
    services.SetupOptions(configuration);

    services.AddDbContextFactory<KrakenDbContext>(options =>
    {
      options.UseNpgsql(connectionString: configuration.GetConnectionString("Kraken"));
    });

    services.AddTransient<OrderService>();
    services.AddTransient<KrakenApi>();
    services.AddTransient<KrakenClient>();
    services.AddSingleton<MailSenderService>();
    services.AddSingleton<MigrationService>();
    services.AddSingleton<MonthlyReporter>();

    services.AddHostedService<DailyReporter>();
    return services;
  }

  public static IConfigurationManager Setup(
    this IConfigurationManager configManager,
    string environment
  )
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

  public static IServiceCollection SetupOptions(
    this IServiceCollection services,
    IConfiguration configuration
  )
  {
    services
      .AddOptions<Secrets>()
      .Bind(configuration.GetSection(nameof(Secrets)))
      .ValidateOnStart();

    services
      .AddOptions<MailSecrets>()
      .Bind(configuration.GetSection(nameof(MailSecrets)))
      .ValidateOnStart();

    services
      .AddOptions<MailOptions>()
      .Bind(configuration.GetSection(nameof(MailOptions)))
      .ValidateOnStart();

    services.AddSingleton<IValidateOptions<Secrets>, SecretsValidator>();
    services.AddSingleton<IValidateOptions<MailSecrets>, MailSecretsValidator>();
    services.AddSingleton<IValidateOptions<MailOptions>, MailOptionsValidator>();

    return services;
  }
}
