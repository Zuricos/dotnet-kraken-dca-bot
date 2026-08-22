using Kbot.MailService.Database;
using Kbot.MailService.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kbot.MailService.Test;

/// <summary>
/// The database connection string used to be baked into <c>appsettings.json</c> (H-11), so a missing
/// configuration value silently fell back to a password published on GitHub. It is now required, and
/// these tests pin the fail-fast behaviour that replaced the fallback.
/// </summary>
[TestClass]
public class ConnectionStringGuardTest
{
  [TestMethod]
  public void Setup_WithoutConnectionString_ThrowsNamingTheSetting()
  {
    var configuration = new ConfigurationBuilder().Build();

    var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
      new ServiceCollection().Setup(configuration)
    );

    StringAssert.Contains(
      exception.Message,
      "ConnectionStrings:Kraken",
      "The message has to name the setting the operator must supply."
    );
  }

  [TestMethod]
  public void Setup_WithBlankConnectionString_ThrowsNamingTheSetting()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection([new KeyValuePair<string, string?>("ConnectionStrings:Kraken", "  ")])
      .Build();

    var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
      new ServiceCollection().Setup(configuration)
    );

    StringAssert.Contains(exception.Message, "ConnectionStrings:Kraken");
  }

  [TestMethod]
  public void Setup_WithConnectionString_RegistersTheServices()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection([
        new KeyValuePair<string, string?>(
          "ConnectionStrings:Kraken",
          "Host=kraken-database;Database=Kbot;Username=postgres;Password=irrelevant"
        ),
      ])
      .Build();

    var services = new ServiceCollection().Setup(configuration);

    Assert.IsTrue(
      services.Any(s => s.ServiceType == typeof(MigrationService)),
      "A configured connection string must let the mail service compose normally."
    );
  }
}
