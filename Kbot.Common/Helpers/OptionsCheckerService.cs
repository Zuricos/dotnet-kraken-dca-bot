using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kbot.Common.Helpers;

public class OptionsCheckerService<T>(ILogger<OptionsCheckerService<T>> logger, IOptions<T> options) : IHostedService where T : class
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            CheckSecrets();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            Environment.Exit(1);
        }
        return Task.CompletedTask;
    }

    private void CheckSecrets()
    {
        var value = options.Value ?? throw new ArgumentException($"OptionsCheckerService for {typeof(T)}: options are not set");
        bool fail = false;
        foreach (var property in typeof(T).GetProperties())
        {

            try
            {
                CheckProperty(value, property);
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
                fail = true;
            }
        }
        if (fail)
        {
            Environment.Exit(1);
        }
    }

    // public for testing
    protected static void CheckProperty(T value, PropertyInfo property)
    {
        var prop = property.GetValue(value) ?? throw new ArgumentException($"OptionsCheckerService for {typeof(T)}: {property.Name} is not set");
        var errorMessage = prop switch
        {
            string s when string.IsNullOrWhiteSpace(s) => $"OptionsCheckerService for {typeof(T)}: {property.Name} is empty",
            DateTime dt when dt == DateTime.MinValue => $"OptionsCheckerService for {typeof(T)}: {property.Name} is DateTime.MinValue",
            TimeSpan ts when ts == TimeSpan.Zero => $"OptionsCheckerService for {typeof(T)}: {property.Name} is TimeSpan.Zero",
            _ => null
        };

        if (errorMessage != null)
        {
            throw new ArgumentException(errorMessage);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
