using System.Reflection;
using Kbot.Common.Api;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Options;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kbot.DcaService.Test;

[TestClass]
public class DcaWorkerTest
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
            .AddUserSecrets<Secrets>();

        builder.Logging.AddConsole();
        builder.Services
            .Configure<Secrets>(builder.Configuration.GetSection(nameof(Secrets)))
            .Configure<CultureOptions>(builder.Configuration.GetSection(nameof(CultureOptions)))
            .Configure<OrderOptions>(builder.Configuration.GetSection(nameof(OrderOptions)))
            .Configure<BalanceOptions>(builder.Configuration.GetSection(nameof(BalanceOptions)))
            .Configure<WaitOptions>(builder.Configuration.GetSection(nameof(WaitOptions)))
            .AddSingleton<HolidayService>()
            .AddTransient<TimeComputeService>()
            .AddTransient<KrakenApi>()
            .AddTransient<KrakenClient>()
            .AddSingleton<DcaWorker>();

        var host = builder.Build();
        _serviceProvider = host.Services;
        host.Start();
    }
    [TestMethod]
    public async Task RunWithCare_ExecuteAsync_DcaWorker()
    {
        if (File.Exists("state.json"))
        {
            File.Delete("state.json");
        }
        var dcaWorker = _serviceProvider.GetRequiredService<DcaWorker>();
        var client = _serviceProvider.GetRequiredService<KrakenClient>();
        var holidaySerivce = _serviceProvider.GetRequiredService<HolidayService>();

        await holidaySerivce.StartAsync(CancellationToken.None);

        var cl_ord_id = $"test-{DateTime.UtcNow:yyMMddHHmmss}";
        try
        {
            dcaWorker.FixOrderId = cl_ord_id;
            var cycle = dcaWorker.StartAsync(CancellationToken.None);
            await Task.Delay(1000);
            await dcaWorker.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Assert.Fail(ex.Message);
        }
        finally
        {
            dcaWorker.FixOrderId = null;
            var cancel = await client.CancelOrder(cl_ord_id, null);
            Assert.IsTrue(cancel, "Cancel order failed! Do it manually!");
        }
    }
}
