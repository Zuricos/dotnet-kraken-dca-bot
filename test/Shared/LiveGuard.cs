namespace Kbot.Testing;

/// <summary>
/// Opt-in gate for tests that hit the live Kraken exchange or send real mail.
/// The category filter in <c>.runsettings</c> keeps them out of a plain <c>dotnet test</c> run;
/// this guard makes sure an explicit <c>--filter</c> cannot arm them by accident either.
/// </summary>
internal static class LiveGuard
{
  private const string OptInVariable = "KBOT_ALLOW_LIVE_TRADING";

  /// <summary>
  /// Aborts the current test as inconclusive unless the caller opted in by setting
  /// <c>KBOT_ALLOW_LIVE_TRADING=1</c>. Call this as the first statement of every test that places
  /// an order or sends mail, or in the <c>[TestInitialize]</c> of a class that only holds such tests.
  /// </summary>
  internal static void RequireOptIn()
  {
    if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
    {
      Assert.Inconclusive(
        $"Skipped: this test trades real money or sends real mail. Set {OptInVariable}=1 to run it."
      );
    }
  }
}
