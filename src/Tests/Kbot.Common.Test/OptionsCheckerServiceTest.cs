using System.Reflection;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kbot.Common.Test;

[TestClass]
public class OptionsCheckerServiceTest
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
      .Configure<TestOptions>(builder.Configuration.GetSection("irrelevant"))
      .AddSingleton<OptionsCheckerService<CultureOptions>>()
      .AddSingleton<TestOptionsCheckerService>();
    var host = builder.Build();
    _serviceProvider = host.Services;
    host.Start();
  }

  [TestMethod]
  public async Task CheckCultureInfos()
  {
    var service = _serviceProvider.GetRequiredService<OptionsCheckerService<CultureOptions>>();
    await service.StartAsync(CancellationToken.None);
  }

  [TestMethod]
  public void CheckTestOptions_ArgumentException()
  {
    var value = _serviceProvider.GetRequiredService<IOptions<TestOptions>>().Value;
    Assert.IsNotNull(value);
    var properties = typeof(TestOptions).GetProperties();
    foreach (var property in properties)
    {
      Assert.ThrowsException<ArgumentException>(() =>
        TestOptionsCheckerService.CheckPropertyForTest(value, property)
      );
    }
  }
}

file class TestOptionsCheckerService(
  ILogger<TestOptionsCheckerService> logger,
  IOptions<TestOptions> options
) : OptionsCheckerService<TestOptions>(logger, options)
{
  public static void CheckPropertyForTest(TestOptions value, PropertyInfo propertyInfo)
  {
    CheckProperty(value, propertyInfo);
  }
}

file record TestOptions
{
  public string StringEmpty { get; init; } = "";
  public string StringWhiteSpace { get; init; } = "   ";
  public string? StringNull { get; init; }
  public float FloatZero { get; init; } = 0;
  public double DoubleZero { get; init; } = 0;
  public int IntZero { get; init; } = 0;
  public long LongZero { get; init; } = 0;
  public decimal DecimalZero { get; init; } = 0;
  public DateTime DateTimeMin { get; init; } = DateTime.MinValue;
  public TimeSpan TimeSpanZero { get; init; } = TimeSpan.Zero;
}
