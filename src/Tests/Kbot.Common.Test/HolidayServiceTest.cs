using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kbot.Common.Test;

[TestClass]
public class HolidayServiceTest
{
  private IServiceProvider _serviceProvider = null!;

  [TestInitialize]
  public void Setup()
  {
    var builder = Host.CreateApplicationBuilder();
    builder.Environment.EnvironmentName = "Development";

    builder
      .Configuration.SetBasePath(AppContext.BaseDirectory)
      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

    builder.Logging.AddConsole();
    builder
      .Services.Configure<CultureOptions>(builder.Configuration.GetSection(nameof(CultureOptions)))
      .AddSingleton<HolidayService>();

    var host = builder.Build();
    _serviceProvider = host.Services;
    host.Start();
  }

  [TestMethod]
  public async Task Is25DecHoliday()
  {
    var service = _serviceProvider.GetRequiredService<HolidayService>();
    await service.StartAsync(CancellationToken.None);
    DateTime utcNow = DateTime.UtcNow;
    var result = service.IsHoliday(new DateTime(utcNow.Year, 12, 25));
    Assert.IsTrue(result);
  }

  [TestMethod]
  public async Task Is28DecHoliday()
  {
    var service = _serviceProvider.GetRequiredService<HolidayService>();
    await service.StartAsync(CancellationToken.None);
    DateTime utcNow = DateTime.UtcNow;
    var result = service.IsHoliday(new DateTime(utcNow.Year, 12, 28));
    Assert.IsFalse(result);
  }

  [TestMethod]
  public async Task ToFarInFuture()
  {
    var service = _serviceProvider.GetRequiredService<HolidayService>();
    await service.StartAsync(CancellationToken.None);
    DateTime utcNow = DateTime.UtcNow;
    Assert.ThrowsExactly<ArgumentException>(() =>
      service.IsHoliday(new DateTime(utcNow.Year + 2, 12, 25))
    );
  }

  [TestMethod]
  public async Task CountyHoliday()
  {
    var service = _serviceProvider.GetRequiredService<HolidayService>();
    await service.StartAsync(CancellationToken.None);
    DateTime utcNow = DateTime.UtcNow;
    var result = service.IsHoliday(new DateTime(utcNow.Year, 5, 1));
    Assert.IsTrue(result);
  }
}
