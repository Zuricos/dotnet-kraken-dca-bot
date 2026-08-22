using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Kbot.DcaService.Options;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Kbot.DcaService.Test;

/// <summary>
/// The top-up day is a configured day-of-month, so it can name a day that a given month does not
/// have: <c>new DateTime(2026, 4, 31)</c> throws, and the throw used to land after a successful
/// order and before the state write (C-4). These tests pin the clamp down.
///
/// No network and no calendar dependency: the holiday cache is seeded by hand, and every case is
/// computed for a year in the past, for which <see cref="HolidayService"/> reports no holidays.
/// Injecting a clock and a holiday source so the *other* tests in this project stop depending on
/// the real calendar is P1-10.
/// </summary>
[TestClass]
public class TopUpDayClampTest
{
  /// <summary>2023 has a 28-day February, 2024 a 29-day one.</summary>
  private static readonly int[] Years = [2023, 2024];

  private static readonly int[] Days = [1, 28, 29, 30, 31];

  [TestMethod]
  public void ComputeNextTopUpTime_NeverThrowsForAnyDayOfMonthInAnyMonth()
  {
    var service = NewService();

    foreach (var year in Years)
    {
      for (var month = 1; month <= 12; month++)
      {
        foreach (var day in Days)
        {
          // Both call sites: the first of the month leaves the top-up day ahead of "now", the last
          // evening of the month puts it behind and takes the month-rollover branch.
          DateTime[] instants =
          [
            new(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
            new(year, month, DateTime.DaysInMonth(year, month), 23, 0, 0, DateTimeKind.Utc),
          ];
          foreach (var utcNow in instants)
          {
            var next = service.ComputeNextTopUpTime(utcNow, day);

            Assert.AreEqual(
              DateTimeKind.Utc,
              next.Kind,
              $"Everything here is compared against DateTime.UtcNow ({utcNow:o}, day {day})."
            );
            Assert.IsGreaterThanOrEqualTo(
              utcNow.Date,
              next,
              $"The next top-up must not be in the past ({utcNow:o}, day {day})."
            );
            Assert.IsLessThan(
              utcNow.Date.AddDays(45),
              next,
              $"The next top-up is at most one month plus a weekend away ({utcNow:o}, day {day})."
            );
            Assert.AreNotEqual(DayOfWeek.Saturday, next.DayOfWeek);
            Assert.AreNotEqual(DayOfWeek.Sunday, next.DayOfWeek);
          }
        }
      }
    }
  }

  [TestMethod]
  public void ComputeNextTopUpTime_ClampsDayThirtyOneToTheMonthLength()
  {
    var service = NewService();

    // February, leap year: the 29th exists and is a Thursday.
    Assert.AreEqual(
      new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc),
      service.ComputeNextTopUpTime(new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), 31)
    );
    // February, non-leap year: the 28th, a Tuesday.
    Assert.AreEqual(
      new DateTime(2023, 2, 28, 0, 0, 0, DateTimeKind.Utc),
      service.ComputeNextTopUpTime(new DateTime(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc), 31)
    );
    // April: clamped to the 30th, which is a Sunday, so the weekend skip moves it to 1 May.
    Assert.AreEqual(
      new DateTime(2023, 5, 1, 0, 0, 0, DateTimeKind.Utc),
      service.ComputeNextTopUpTime(new DateTime(2023, 4, 1, 0, 0, 0, DateTimeKind.Utc), 31)
    );
    // December: the 31st exists but is a Sunday, so the skip crosses into the next year.
    Assert.AreEqual(
      new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
      service.ComputeNextTopUpTime(new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc), 31)
    );
  }

  [TestMethod]
  public void ComputeNextTopUpTime_RollsOverIntoTheNextMonthAndTheNextYear()
  {
    var service = NewService();

    // The rollover used to be a hand-written December special case; January of the next year is the
    // only thing AddMonths on the first of the month can produce here.
    Assert.AreEqual(
      new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
      service.ComputeNextTopUpTime(new DateTime(2023, 12, 31, 23, 0, 0, DateTimeKind.Utc), 1)
    );
    // The case that threw: rolling a day-31 configuration over into February.
    Assert.AreEqual(
      new DateTime(2023, 2, 28, 0, 0, 0, DateTimeKind.Utc),
      service.ComputeNextTopUpTime(new DateTime(2023, 1, 31, 23, 0, 0, DateTimeKind.Utc), 31)
    );
  }

  [TestMethod]
  public void BalanceOptionsValidator_RejectsADayThatDoesNotExistInEveryMonth()
  {
    var validator = new BalanceOptionsValidator();

    var rejected = validator.Validate(
      null,
      new BalanceOptions { DefaultTopupDayOfMonth = 31, ReserveFiat = 0 }
    );

    Assert.IsTrue(rejected.Failed, "Day 31 does not exist in every month and must not start up.");
    Assert.IsTrue(
      rejected.FailureMessage!.Contains("DefaultTopupDayOfMonth")
        && rejected.FailureMessage.Contains("28"),
      $"The message should name the option and the accepted range, got: {rejected.FailureMessage}"
    );
    Assert.IsTrue(
      validator
        .Validate(null, new BalanceOptions { DefaultTopupDayOfMonth = 0, ReserveFiat = 0 })
        .Failed,
      "There is no day 0."
    );
    Assert.IsTrue(
      validator
        .Validate(null, new BalanceOptions { DefaultTopupDayOfMonth = 28, ReserveFiat = 0 })
        .Succeeded,
      "Day 28 exists in every month and must stay a legal configuration."
    );
  }

  /// <summary>
  /// A <see cref="TimeComputeService"/> whose holiday cache is seeded but empty. The seeding matters:
  /// <see cref="HolidayService.IsHoliday"/> reads <c>Holidays.Keys.Min()</c>, which throws on an
  /// empty cache and re-fetches over the network for a stale one.
  /// </summary>
  private static TimeComputeService NewService()
  {
    var holidayService = new HolidayService(
      NullLogger<HolidayService>.Instance,
      MsOptions.Create(
        new CultureOptions
        {
          CultureString = "de-CH",
          CountyCode = "CH-ZH",
          Fiat = "CHF",
        }
      )
    );
    var thisYear = DateTime.UtcNow.Year;
    holidayService.Holidays[thisYear] = [];
    holidayService.Holidays[thisYear + 1] = [];
    return new TimeComputeService(NullLogger<TimeComputeService>.Instance, holidayService);
  }
}
