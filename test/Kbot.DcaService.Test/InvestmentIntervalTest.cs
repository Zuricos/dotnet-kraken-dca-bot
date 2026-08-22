using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Kbot.DcaService.Test;

/// <summary>
/// The divisor guard in <see cref="TimeComputeService.ComputeNextInvestmentInterval"/> (C-2). A
/// zero cost used to make the interval TimeSpan.Zero, which schedules an order immediately and then
/// once per MinWaitTime.
/// </summary>
[TestClass]
public class InvestmentIntervalTest
{
  private static readonly TimeSpan Window = TimeSpan.FromHours(10);

  private static TimeComputeService NewService() =>
    new(
      NullLogger<TimeComputeService>.Instance,
      new HolidayService(
        NullLogger<HolidayService>.Instance,
        MsOptions.Create(
          new CultureOptions
          {
            CultureString = "de-CH",
            CountyCode = "CH-ZH",
            Fiat = "CHF",
          }
        )
      )
    );

  [TestMethod]
  // {zero, negative, positive} balance × {zero, negative, positive} cost.
  [DataRow(0.0, 0.0)]
  [DataRow(0.0, -1.0)]
  [DataRow(0.0, 2.5)]
  [DataRow(-1.0, 0.0)]
  [DataRow(-1.0, -1.0)]
  [DataRow(-1.0, 2.5)]
  [DataRow(900.0, 0.0)]
  [DataRow(900.0, -1.0)]
  [DataRow(900.0, 2.5)]
  // And the inputs no arithmetic survives: not-a-number, infinity, and a cost so small that the
  // interval would round down to zero ticks.
  [DataRow(double.NaN, 2.5)]
  [DataRow(900.0, double.NaN)]
  [DataRow(double.PositiveInfinity, 2.5)]
  [DataRow(900.0, double.PositiveInfinity)]
  [DataRow(double.MaxValue, double.Epsilon)]
  public void ComputeNextInvestmentInterval_IsAlwaysFiniteAndNonZero(
    double balanceFiat,
    double costForVolume
  )
  {
    var interval = NewService().ComputeNextInvestmentInterval(balanceFiat, costForVolume, Window);

    Assert.IsGreaterThan(
      TimeSpan.Zero,
      interval,
      $"balance {balanceFiat} and cost {costForVolume} produced {interval}, which orders at once."
    );
    Assert.IsLessThanOrEqualTo(
      Window,
      interval,
      "An interval must never outlast the top-up window it spreads investments over."
    );
  }

  [TestMethod]
  public void ComputeNextInvestmentInterval_SpreadsTheBudgetOverTheWindow()
  {
    var interval = NewService().ComputeNextInvestmentInterval(100.0, 10.0, Window);

    Assert.AreEqual(Window / 10, interval, "10 affordable orders should be one per tenth window.");
  }

  [TestMethod]
  public void ComputeNextInvestmentInterval_WaitsTheWholeWindowWhenNothingIsAffordable()
  {
    var interval = NewService().ComputeNextInvestmentInterval(5.0, 10.0, Window);

    Assert.AreEqual(Window, interval, "A budget below one order cannot be spread out further.");
  }

  [TestMethod]
  [DataRow(0)]
  [DataRow(-1)]
  public void ComputeNextInvestmentInterval_SchedulesNothingWithoutATopUpWindow(int windowHours)
  {
    var interval = NewService()
      .ComputeNextInvestmentInterval(900.0, 2.5, TimeSpan.FromHours(windowHours));

    // MaxValue keeps the caller's clamp in charge: it becomes MaxWaitTime, never an immediate order.
    Assert.AreEqual(TimeSpan.MaxValue, interval);
  }
}
