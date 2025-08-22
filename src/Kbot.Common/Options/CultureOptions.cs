using System.Globalization;
using Microsoft.Extensions.Options;

namespace Kbot.Common.Options;

public record CultureOptions
{
  public string CultureString { get; init; } = "";
  public string Fiat { get; init; } = "";
  public string CountyCode { get; init; } = "";

  public CultureInfo CultureInfo => new(CultureString);
}

public class CultureOptionsValidator : IValidateOptions<CultureOptions>
{
  public ValidateOptionsResult Validate(string? name, CultureOptions options)
  {
    List<string> vor = [];

    if (string.IsNullOrEmpty(options.CultureString))
    {
      vor.Add("CultureString must be set");
    }
    if (string.IsNullOrEmpty(options.Fiat))
    {
      vor.Add("Fiat must be set");
    }
    if (string.IsNullOrEmpty(options.CountyCode))
    {
      vor.Add("CountyCode must be set");
    }
    if (vor.Count > 0)
    {
      return ValidateOptionsResult.Fail("CultureOptions incomplete: " + string.Join(", ", vor));
    }
    return ValidateOptionsResult.Success;
  }
}
