using Kbot.Common.Helpers;
using Kbot.DcaService.Models;

namespace Kbot.DcaService.Utility;

public class TimeComputeService(
    ILogger<TimeComputeService> logger,
    HolidayService holidayService)
{

    public DateTime ComputeNextTopUpTime(DateTime utcNow, int topUpDayOfMonth)
    {
        var nextTopUpTime = new DateTime(utcNow.Year, utcNow.Month, topUpDayOfMonth, 0, 0, 0);

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

        while (nextTopUpTime.DayOfWeek == DayOfWeek.Saturday
                || nextTopUpTime.DayOfWeek == DayOfWeek.Sunday
                || holidayService.IsHoliday(nextTopUpTime))
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

    public TimeSpan ComputeNextInvestmentInterval(double balanceFiat, double costForVolume, TimeSpan timeUntilNextTopUp)
    {
        var maxNrOfInvestmentsUntilNextTopUp = balanceFiat / costForVolume;
        var currentInvertval = timeUntilNextTopUp / maxNrOfInvestmentsUntilNextTopUp;
        return currentInvertval;
    }
}
