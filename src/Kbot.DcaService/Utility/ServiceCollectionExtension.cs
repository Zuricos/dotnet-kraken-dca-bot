using System.Reflection;
using Kbot.Common.Api;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Options;
using Microsoft.Extensions.Options;

namespace Kbot.DcaService.Utility;

public static class ServiceCollectionExtension
{
  public static IServiceCollection Setup(
    this IServiceCollection services,
    IConfiguration configuration
  )
  {
    services.SetupOptions(configuration);

    services.AddTransient<KrakenApi>();
    services.AddTransient<KrakenClient>();
    services.AddTransient<TimeComputeService>();
    services.AddSingleton<HolidayService>();

    services.AddHostedService(c => c.GetRequiredService<HolidayService>());
    services.AddHostedService<DcaWorker>();

    services.AddOptions();
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
      .AddJsonFile("/run/secrets/dca-secrets", optional: true)
      .AddUserSecrets(Assembly.GetExecutingAssembly())
      .AddUserSecrets<Secrets>()
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
      .AddOptions<OrderOptions>()
      .Bind(configuration.GetSection(nameof(OrderOptions)))
      .ValidateOnStart();

    services
      .AddOptions<BalanceOptions>()
      .Bind(configuration.GetSection(nameof(BalanceOptions)))
      .ValidateOnStart();

    services
      .AddOptions<WaitOptions>()
      .Bind(configuration.GetSection(nameof(WaitOptions)))
      .ValidateOnStart();

    services
      .AddOptions<CultureOptions>()
      .Bind(configuration.GetSection(nameof(CultureOptions)))
      .ValidateOnStart();

    services.AddSingleton<IValidateOptions<Secrets>, SecretsValidator>();
    services.AddSingleton<IValidateOptions<OrderOptions>, OrderOptionsValidator>();
    services.AddSingleton<IValidateOptions<BalanceOptions>, BalanceOptionsValidator>();
    services.AddSingleton<IValidateOptions<WaitOptions>, WaitOptionsValidator>();
    services.AddSingleton<IValidateOptions<CultureOptions>, CultureOptionsValidator>();

    return services;
  }
}
