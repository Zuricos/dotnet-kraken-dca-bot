using Kbot.Common.Options;
using Kbot.DcaService.Options;
using Kbot.DcaService.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kbot.DcaService.Test;

/// <summary>
/// Runs the real <c>src/Kbot.DcaService/appsettings.json</c> — copied into the output as
/// <c>dca-appsettings.json</c> by this project's csproj — through the real
/// <see cref="ServiceCollectionExtension.SetupOptions"/> wiring.
///
/// This is the H-10 acceptance criterion as a test: the shipped file used to contain nothing but the
/// Serilog block, so an operator who forgot <c>stack.env</c> started up with
/// <c>MinWaitTime = 00:00:00</c> and a busy loop. Now the defaults must be complete <em>and</em>
/// valid, and the only things a fresh checkout still has to be told are the secrets and which pair
/// to buy. Asserting it through the container rather than against the validators directly also
/// covers the registration: a validator nobody registers is a validator nobody runs.
/// </summary>
[TestClass]
public class ShippedDefaultsTest
{
  private const string ShippedAppSettings = "dca-appsettings.json";

  [TestMethod]
  public void ShippedDefaults_AreCompleteAndValidForEverySectionButTheDeliberateOnes()
  {
    var provider = Build();

    var wait = provider.GetRequiredService<IOptions<WaitOptions>>().Value;
    Assert.AreEqual(TimeSpan.FromSeconds(30), wait.MinWaitTime);
    Assert.AreEqual(TimeSpan.FromHours(1), wait.MaxWaitTime);

    var balance = provider.GetRequiredService<IOptions<BalanceOptions>>().Value;
    Assert.AreEqual(26, balance.DefaultTopupDayOfMonth);
    Assert.AreEqual(0, balance.ReserveFiat);

    var culture = provider.GetRequiredService<IOptions<CultureOptions>>().Value;
    Assert.AreEqual("CHF", culture.Fiat);
  }

  /// <summary>
  /// Which asset the bot buys, and with whose keys, must stay a deliberate choice — so these two are
  /// the only startup failures a checkout with no <c>stack.env</c> may produce.
  /// </summary>
  [TestMethod]
  public void ShippedDefaults_FailOnlyOnTheCryptoPairAndTheSecrets()
  {
    var provider = Build();

    var orderFailure = Assert.ThrowsExactly<OptionsValidationException>(() =>
      _ = provider.GetRequiredService<IOptions<OrderOptions>>().Value
    );
    var message = string.Join(" ", orderFailure.Failures);
    StringAssert.Contains(message, nameof(OrderOptions.CryptoPair));
    foreach (
      var defaulted in new[]
      {
        nameof(OrderOptions.Type),
        nameof(OrderOptions.Fee),
        nameof(OrderOptions.MinOrderVolume),
        nameof(OrderOptions.AskMultiplier),
      }
    )
    {
      Assert.IsFalse(
        message.Contains(defaulted, StringComparison.Ordinal),
        $"{defaulted} has a shipped default, so it must not appear in the startup error: {message}"
      );
    }

    Assert.ThrowsExactly<OptionsValidationException>(
      () => _ = provider.GetRequiredService<IOptions<Secrets>>().Value,
      "The Kraken keys are never shipped, so they must be the other startup failure."
    );
  }

  [TestMethod]
  public void ShippedDefaults_PlusACryptoPairValidateCompletely()
  {
    var provider = Build(("OrderOptions:CryptoPair", "XBTCHF"));

    var order = provider.GetRequiredService<IOptions<OrderOptions>>().Value;

    Assert.AreEqual("XBTCHF", order.CryptoPair);
    Assert.AreEqual(1.00001, order.AskMultiplier);
  }

  private static ServiceProvider Build(params (string Key, string Value)[] overrides)
  {
    var configuration = new ConfigurationBuilder()
      .AddJsonFile(ShippedAppSettings, optional: false)
      .AddInMemoryCollection(
        overrides.Select(o => new KeyValuePair<string, string?>(o.Key, o.Value))
      )
      .Build();

    return new ServiceCollection().AddLogging().SetupOptions(configuration).BuildServiceProvider();
  }
}
