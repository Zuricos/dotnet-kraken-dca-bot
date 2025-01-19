using System.Reflection;
using Kbot.Common.Api;
using Kbot.Common.Options;
using Kbot.MailService.Database;
using Kbot.MailService.Options;
using Kbot.MailService.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kbot.MailService.Test;

[TestClass]
public class MailGenerateTest
{

    private IServiceProvider _serviceProvider = null!;

    [TestInitialize]
    public void Setup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = "Development";

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .AddUserSecrets<Secrets>()
            .AddUserSecrets<MailSecrets>();

        builder.Logging.AddConsole();
        builder.Services
            .Configure<Secrets>(builder.Configuration.GetSection(nameof(Secrets)))
            .Configure<MailSecrets>(builder.Configuration.GetSection(nameof(MailSecrets)))
            .Configure<MailOptions>(builder.Configuration.GetSection(nameof(MailOptions)))
            .AddDbContextFactory<KrakenDbContext>(c => c.UseInMemoryDatabase("KrakenDb"))
            .AddTransient<KrakenApi>()
            .AddTransient<KrakenClient>()
            .AddTransient<OrderService>()
            .AddTransient<MailSenderService>()
            .AddSingleton<MigrationService>();

        var host = builder.Build();
        _serviceProvider = host.Services;
        host.Start();
    }

    [TestMethod]
    public async Task SendTestMail()
    {
        var mailservice = _serviceProvider.GetRequiredService<MailSenderService>();
        var testString = """
            This is a Mail from my MailService.Test.
        """;

        await mailservice.SendMail(testString, "TEST");
    }

    [TestMethod]
    public async Task SendMailLast24Hours()
    {
        var migrationService = _serviceProvider.GetRequiredService<MigrationService>();
        await migrationService.StartAsync(CancellationToken.None);
        var mailservice = _serviceProvider.GetRequiredService<MailSenderService>();
        await mailservice.SendMailWithClosedOrdersLast24Hours();
    }

    [TestMethod]
    public async Task SendReportMail()
    {
        var migrationService = _serviceProvider.GetRequiredService<MigrationService>();
        await migrationService.StartAsync(CancellationToken.None);

        var orderService = _serviceProvider.GetRequiredService<OrderService>();
        await orderService.FetchClosedOrdersAndSave(DateTimeOffset.UtcNow.AddDays(-2));
    
        var mailservice = _serviceProvider.GetRequiredService<MailSenderService>();
        await mailservice.SendReportMail(DateTimeOffset.UtcNow.AddDays(-2));
    }
}
