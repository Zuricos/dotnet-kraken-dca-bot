using Kbot.Common.Helpers;
using Kbot.DcaService.Models;

namespace Kbot.DcaService.Utility;

public class TimeComputeService(ILogger<TimeComputeService> logger, HolidayService holidayService)
{
  /// <summary>
  /// The top-up day as an instant in the given month, clamped to that month's length: day 31 in
  /// April is the 30th and day 29-31 in a non-leap February is the 28th. Plain
  /// <c>new DateTime(2026, 4, 31)</c> throws, and the throw used to land between a sent order and
  /// the state write (C-4). <see cref="DateTimeKind.Utc"/> because every comparison here is against
  /// <see cref="DateTime.UtcNow"/>.
  /// </summary>
  private static DateTime AtDayOfMonth(int year, int month, int day) =>
    new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), 0, 0, 0, DateTimeKind.Utc);

  public DateTime ComputeNextTopUpTime(DateTime utcNow, int topUpDayOfMonth)
  {
    var nextTopUpTime = AtDayOfMonth(utcNow.Year, utcNow.Month, topUpDayOfMonth);

    while (
      nextTopUpTime.DayOfWeek == DayOfWeek.Saturday
      || nextTopUpTime.DayOfWeek == DayOfWeek.Sunday
      || holidayService.IsHoliday(nextTopUpTime)
    )
    {
      nextTopUpTime = nextTopUpTime.AddDays(1);
    }

    if (utcNow > nextTopUpTime)
    {
      // Rolling over through the first of the month keeps December from needing its own branch and
      // keeps the clamp: AddMonths on the 1st can never overflow into the month after next.
      var firstOfNextMonth = new DateTime(
        utcNow.Year,
        utcNow.Month,
        1,
        0,
        0,
        0,
        DateTimeKind.Utc
      ).AddMonths(1);
      nextTopUpTime = AtDayOfMonth(firstOfNextMonth.Year, firstOfNextMonth.Month, topUpDayOfMonth);
    }

    while (
      nextTopUpTime.DayOfWeek == DayOfWeek.Saturday
      || nextTopUpTime.DayOfWeek == DayOfWeek.Sunday
      || holidayService.IsHoliday(nextTopUpTime)
    )
    {
      nextTopUpTime = nextTopUpTime.AddDays(1);
    }
    logger.LogInformation("Next top up time: {nextTopUpTime}", nextTopUpTime);
    return nextTopUpTime;
  }

  public DcaState ComputeTimeUntilNextTopUp(DcaState state, int defaultTopupDayOfMonth)
  {
    var utcNow = DateTime.UtcNow;
    var nextTopUpTime = state.NextTopUpTime;
    if (utcNow > nextTopUpTime)
    {
      nextTopUpTime = ComputeNextTopUpTime(utcNow, defaultTopupDayOfMonth);
      state = state with { NextTopUpTime = nextTopUpTime };
    }
    var timeUntilNextTopUp = nextTopUpTime - utcNow;
    return state with { TimeUntilNextTopUp = timeUntilNextTopUp };
  }

  public TimeSpan ComputeNextInvestmentInterval(
    double balanceFiat,
    double costForVolume,
    TimeSpan timeUntilNextTopUp
  )
  {
    // Every degenerate input is answered with a finite, non-zero interval: dividing by a zero cost
    // yields Infinity, and TimeSpan.Zero / Infinity is TimeSpan.Zero, which schedules an order
    // immediately and then once per MinWaitTime.
    if (timeUntilNextTopUp <= TimeSpan.Zero)
    {
      logger.LogWarning(
        "No top-up window to spread investments over ({TimeUntilNextTopUp}); nothing to schedule.",
        timeUntilNextTopUp
      );
      return TimeSpan.MaxValue;
    }
    if (
      balanceFiat <= 0
      || costForVolume <= 0
      || !double.IsFinite(balanceFiat)
      || !double.IsFinite(costForVolume)
    )
    {
      logger.LogWarning(
        "Cannot compute an investment interval from balance {BalanceFiat} and cost {CostForVolume}; "
          + "spreading over the whole top-up window instead.",
        balanceFiat,
        costForVolume
      );
      return timeUntilNextTopUp;
    }

    var maxNrOfInvestmentsUntilNextTopUp = balanceFiat / costForVolume;
    if (maxNrOfInvestmentsUntilNextTopUp < 1)
    {
      return timeUntilNextTopUp;
    }
    var currentInvertval = timeUntilNextTopUp / maxNrOfInvestmentsUntilNextTopUp;
    // A count large enough to round the interval down to zero would order at MinWaitTime forever;
    // the whole window is the safe answer for an input that degenerate.
    return currentInvertval <= TimeSpan.Zero ? timeUntilNextTopUp : currentInvertval;
  }
}
