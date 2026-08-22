using Kbot.Common.Options;

namespace Kbot.Common.Test;

/// <summary>
/// H-10 survived because not one of the five options validators had a test. These pin the three
/// rules <see cref="CultureOptionsValidator"/> has today; checking that
/// <see cref="CultureOptions.CultureString"/> and <see cref="CultureOptions.CountyCode"/> name a
/// culture and a region that actually exist — <c>new CultureInfo("\"de-CH\"")</c> throws — is M-15,
/// owned by P4-10.
/// </summary>
[TestClass]
public class CultureOptionsValidatorTest
{
  private static readonly CultureOptions Sane = new()
  {
    CultureString = "de-CH",
    CountyCode = "CH-ZH",
    Fiat = "CHF",
  };

  [TestMethod]
  public void RejectsAMissingCultureString()
  {
    AssertFails(Sane with { CultureString = "" }, nameof(CultureOptions.CultureString));
  }

  [TestMethod]
  public void RejectsAMissingFiat()
  {
    AssertFails(Sane with { Fiat = "" }, nameof(CultureOptions.Fiat));
  }

  [TestMethod]
  public void RejectsAMissingCountyCode()
  {
    AssertFails(Sane with { CountyCode = "" }, nameof(CultureOptions.CountyCode));
  }

  [TestMethod]
  public void RejectsAnEmptySectionNamingEveryMissingOption()
  {
    var result = new CultureOptionsValidator().Validate(null, new CultureOptions());

    Assert.IsTrue(result.Failed, "An omitted CultureOptions section must not start up.");
    // One startup, one complete list: the operator should not have to fix these one restart at a
    // time.
    StringAssert.Contains(result.FailureMessage!, nameof(CultureOptions.CultureString));
    StringAssert.Contains(result.FailureMessage!, nameof(CultureOptions.Fiat));
    StringAssert.Contains(result.FailureMessage!, nameof(CultureOptions.CountyCode));
  }

  [TestMethod]
  public void AcceptsAFullyConfiguredSection()
  {
    Assert.IsTrue(new CultureOptionsValidator().Validate(null, Sane).Succeeded);
  }

  private static void AssertFails(CultureOptions options, string expectedOptionName)
  {
    var result = new CultureOptionsValidator().Validate(null, options);

    Assert.IsTrue(result.Failed, $"Should not have started up with {options}.");
    StringAssert.Contains(
      result.FailureMessage!,
      expectedOptionName,
      $"The startup error is all the operator gets, so it must name {expectedOptionName}. "
        + $"Got: {result.FailureMessage}"
    );
  }
}
