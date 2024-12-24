using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Kbot.Common.Models;
using Kbot.Common.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kbot.Common.Helpers;

public class HolidayService(ILogger<HolidayService> logger, IOptions<CultureOptions> cultureInfo) : IHostedService
{
    public ConcurrentDictionary<int, List<PublicHoliday>> Holidays { get; set; } = [];
    public string CountryCode => new RegionInfo(cultureInfo.Value.CultureInfo.Name).TwoLetterISORegionName;
    public string CountyCode => cultureInfo.Value.CountyCode;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var nextYear = now.AddYears(1).Year;
        await FetchHolidaysFromRemote(now.Year, cancellationToken);
        logger.LogInformation("Holidays for this year fetched");
        await FetchHolidaysFromRemote(nextYear, cancellationToken);
        logger.LogInformation("Holidays for next year fetched");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public bool IsHoliday(DateTime date)
    {
        var utcNow = DateTime.UtcNow;

        if (Holidays.Keys.Min() < utcNow.Year)
        {
            UpdateCachedHolidays();
        }

        if (date.Year - 1 > utcNow.Year)
        {
            throw new ArgumentException("Date is too far in the future");
        }


        if (Holidays.TryGetValue(date.Year, out var holidays))
        {
            return holidays.Any(h => h.Date.Date == date.Date);
        }
        return false;
    }

    public void UpdateCachedHolidays()
    {
        var now = DateTime.UtcNow;
        var nextYear = now.AddYears(1).Year;
        FetchHolidaysFromRemote(nextYear).Wait();
        Holidays.TryRemove(Holidays.Keys.Min(), out _);
    }

    private async Task FetchHolidaysFromRemote(int year, CancellationToken cancellationToken = default)
    {
        try
        {
            var jsonSerializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync($"https://date.nager.at/api/v3/publicholidays/{year}/{CountryCode}", cancellationToken);
            if (!response.IsSuccessStatusCode) throw new Exception("Failed to fetch holidays for year " + year);

            using var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var publicHolidays = JsonSerializer.Deserialize<PublicHoliday[]>(jsonStream, jsonSerializerOptions) ?? throw new Exception("Failed to deserialize holidays for year " + year);

            var filteredPublicHolidays = publicHolidays
                .Where(h => (h.Counties == null || h.Counties.Contains(CountyCode)) && h.Types.Contains("Public")).ToList();

            Holidays.TryAdd(year, filteredPublicHolidays);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching holidays");
        }
    }
}
