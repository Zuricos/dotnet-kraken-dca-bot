using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Models;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kbot.DcaService.Test;

[TestClass]
public class TimeComputeTest
{
    private IServiceProvider _serviceProvider = null!;

    [TestInitialize]
    public void Setup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = "Development";

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        builder.Logging.AddConsole();
        builder.Services
            .Configure<CultureOptions>(builder.Configuration.GetSection(nameof(CultureOptions)))
            .AddSingleton<HolidayService>()
            .AddTransient<TimeComputeService>();

        var host = builder.Build();
        _serviceProvider = host.Services;
        host.Start();
    }

    [TestMethod]
    public async Task ComputeNextTopUp_WeekDayTopUpDay()
    {
        var holidayService = _serviceProvider.GetRequiredService<HolidayService>();
        var timeComputeService = _serviceProvider.GetRequiredService<TimeComputeService>();
        await holidayService.StartAsync(CancellationToken.None);
        var utcNow = DateTime.UtcNow;
        var dayOfMonth = 10;
        var nowTest = new DateTime(utcNow.Year, 7, 1);
        var topUpDay = new DateTime(utcNow.Year, 7, dayOfMonth);
        while (topUpDay.DayOfWeek == DayOfWeek.Saturday || topUpDay.DayOfWeek == DayOfWeek.Sunday)
        {
            topUpDay = topUpDay.AddDays(1);
        }

        var nextTopUp = timeComputeService.ComputeNextTopUpTime(nowTest, dayOfMonth);
        Assert.AreEqual(topUpDay, nextTopUp);
    }

    [TestMethod]
    public async Task ComputeNextTopUp_WeekendDayTopUpDay()
    {
        var holidayService = _serviceProvider.GetRequiredService<HolidayService>();
        var timeComputeService = _serviceProvider.GetRequiredService<TimeComputeService>();
        await holidayService.StartAsync(CancellationToken.None);
        var utcNow = DateTime.UtcNow;
        var dayOfMonth = 10;
        var nowTest = new DateTime(utcNow.Year, 7, 1);
        var topUpDay = new DateTime(utcNow.Year, 7, dayOfMonth);
        while (topUpDay.DayOfWeek != DayOfWeek.Saturday && topUpDay.DayOfWeek != DayOfWeek.Sunday)
        {
            dayOfMonth++;
            topUpDay = topUpDay.AddDays(1);
        }
        while (topUpDay.DayOfWeek == DayOfWeek.Saturday || topUpDay.DayOfWeek == DayOfWeek.Sunday)
        {
            topUpDay = topUpDay.AddDays(1);
        }

        var nextTopUp = timeComputeService.ComputeNextTopUpTime(nowTest, dayOfMonth);
        Assert.AreEqual(topUpDay, nextTopUp);
    }

    [TestMethod]
    public async Task ComputeNextTopUp_HolidayTopUpDay()
    {
        var holidayService = _serviceProvider.GetRequiredService<HolidayService>();
        var timeComputeService = _serviceProvider.GetRequiredService<TimeComputeService>();
        await holidayService.StartAsync(CancellationToken.None);
        var utcNow = DateTime.UtcNow;
        var dayOfMonth = 25;
        var nowTest = new DateTime(utcNow.Year, 12, 1);
        var topUpDay = new DateTime(utcNow.Year, 12, dayOfMonth + 2);
        while (topUpDay.DayOfWeek == DayOfWeek.Saturday || topUpDay.DayOfWeek == DayOfWeek.Sunday)
        {
            topUpDay = topUpDay.AddDays(1);
        }

        var nextTopUp = timeComputeService.ComputeNextTopUpTime(nowTest, dayOfMonth);
        Assert.AreEqual(topUpDay, nextTopUp);
    }

    [TestMethod]
    public async Task ComputeTimeUntilNextTopUp_Tomorrow()
    {
        var holidayService = _serviceProvider.GetRequiredService<HolidayService>();
        var timeComputeService = _serviceProvider.GetRequiredService<TimeComputeService>();
        await holidayService.StartAsync(CancellationToken.None);
        var utcNow = DateTime.UtcNow;
        var topUpDay = utcNow.AddDays(1);
        while (topUpDay.DayOfWeek == DayOfWeek.Saturday || topUpDay.DayOfWeek == DayOfWeek.Sunday)
        {
            topUpDay = topUpDay.AddDays(1);
        }

        var state = new DcaState()
        {
            NextTopUpTime = topUpDay,
            TimeUntilNextTopUp = topUpDay - utcNow,
            LastInvestmentTime = utcNow
        };
        var result = timeComputeService.ComputeTimeUntilNextTopUp(state, topUpDay.Day);
        Assert.AreEqual(topUpDay.Year, result.NextTopUpTime.Year);
        Assert.AreEqual(topUpDay.Month, result.NextTopUpTime.Month);
        Assert.AreEqual(topUpDay.Day, result.NextTopUpTime.Day);

        Assert.IsTrue(result.NextTopUpTime - utcNow - TimeSpan.FromMinutes(1) < result.TimeUntilNextTopUp);
        Assert.IsTrue(result.NextTopUpTime - utcNow + TimeSpan.FromMinutes(1) > result.TimeUntilNextTopUp);
    }

    [TestMethod]
    public async Task ComputeTimeUntilNextTopUp_Yesterday()
    {
        var holidayService = _serviceProvider.GetRequiredService<HolidayService>();
        var timeComputeService = _serviceProvider.GetRequiredService<TimeComputeService>();
        await holidayService.StartAsync(CancellationToken.None);
        var utcNow = DateTime.UtcNow;
        var topUpDay = utcNow.AddDays(-1);

        var state = new DcaState()
        {
            NextTopUpTime = topUpDay,
            TimeUntilNextTopUp = topUpDay - utcNow,
            LastInvestmentTime = utcNow
        };

        if (topUpDay.Month == 12)
        {
            topUpDay = new DateTime(topUpDay.Year + 1, 1, topUpDay.Day);
        }
        else
        {
            topUpDay = new DateTime(topUpDay.Year, topUpDay.Month + 1, topUpDay.Day);
        }

        var result = timeComputeService.ComputeTimeUntilNextTopUp(state, topUpDay.Day);
        Assert.AreEqual(topUpDay.Year, result.NextTopUpTime.Year);
        Assert.AreEqual(topUpDay.Month, result.NextTopUpTime.Month);
        Assert.AreEqual(topUpDay.Day, result.NextTopUpTime.Day);

        Assert.IsTrue(result.NextTopUpTime - utcNow - TimeSpan.FromMinutes(1) < result.TimeUntilNextTopUp);
        Assert.IsTrue(result.NextTopUpTime - utcNow + TimeSpan.FromMinutes(1) > result.TimeUntilNextTopUp);
    }

    [TestMethod]
    public void ComputeNextInvestmentInterval()
    {
        var timeComputeService = _serviceProvider.GetRequiredService<TimeComputeService>();
        var balanceFiat = 1000;
        var costForVolume = 100;
        var timeUntilNextTopUp = TimeSpan.FromDays(30);
        var result = timeComputeService.ComputeNextInvestmentInterval(balanceFiat, costForVolume, timeUntilNextTopUp);
        Assert.AreEqual(TimeSpan.FromDays(3), result);
    }
}