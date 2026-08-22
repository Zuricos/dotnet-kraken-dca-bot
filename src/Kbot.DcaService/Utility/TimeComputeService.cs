using Kbot.Common.Helpers;
using Kbot.DcaService.Models;

namespace Kbot.DcaService.Utility;

public class TimeComputeService(ILogger<TimeComputeService> logger, HolidayService holidayService)
{
  public DateTime ComputeNextTopUpTime(DateTime utcNow, int topUpDayOfMonth)
  {
    var nextTopUpTime = new DateTime(utcNow.Year, utcNow.Month, topUpDayOfMonth, 0, 0, 0);

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
      if (utcNow.Month == 12)
      {
        nextTopUpTime = new DateTime(utcNow.Year + 1, 1, topUpDayOfMonth);
      }
      else
      {
        nextTopUpTime = new DateTime(utcNow.Year, utcNow.Month + 1, topUpDayOfMonth);
      }
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
