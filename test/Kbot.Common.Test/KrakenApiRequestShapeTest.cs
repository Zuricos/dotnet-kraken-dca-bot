using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kbot.Common.Api;
using Kbot.Common.Dtos;
using Kbot.Common.Enums;
using Kbot.Common.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Kbot.Common.Test;

/// <summary>
/// Covers the request that <see cref="ApiTestPublic.TestSendAndCancelBuyOrder"/> used to verify by
/// actually buying bitcoin: an AddOrder call is asserted here against a stub transport, so the
/// signed request shape is checked without any network access or funds at risk.
/// </summary>
[TestClass]
public class KrakenApiRequestShapeTest
{
  private const string ApiKey = "test-api-key";
  private static readonly string ApiSecret = Convert.ToBase64String(
    Encoding.UTF8.GetBytes("test-api-secret")
  );

  private static readonly string TickerResponse = JsonSerializer.Serialize(
    new
    {
      error = Array.Empty<string>(),
      result = new Dictionary<string, object>
      {
        ["XBTCHF"] = new
        {
          a = new[] { "50123.40000", "1", "1.000" },
          b = new[] { "50100.10000", "2", "2.000" },
          c = new[] { "50110.00000", "0.00100000" },
          v = new[] { "10.00000000", "20.00000000" },
          p = new[] { "50000.00000", "50050.00000" },
          t = new[] { 100, 200 },
          l = new[] { "49000.00000", "48000.00000" },
          h = new[] { "51000.00000", "52000.00000" },
          o = "50000.00000",
        },
      },
    }
  );

  private static readonly string AddOrderResponse = JsonSerializer.Serialize(
    new
    {
      error = Array.Empty<string>(),
      result = new
      {
        txid = new[] { "OAV6BB-Q2SHQ-XSCJPG" },
        descr = new { order = "buy 0.00005000 XBTCHF @ limit 50000.0" },
      },
    }
  );

  [TestMethod]
  public async Task SendOrder_SignsAndSerializesTheRequest()
  {
    var handler = new RecordingHandler(AddOrderResponse);
    var api = new KrakenApi(
      NullLogger<KrakenApi>.Instance,
      MsOptions.Create(new Secrets { ApiKey = ApiKey, ApiSecret = ApiSecret }),
      handler
    );
    using var client = new KrakenClient(NullLogger<KrakenClient>.Instance, api);

    var accepted = await client.SendOrder(
      new OrderRequest
      {
        Pair = "XBTCHF",
        Type = BuyOrSell.Buy,
        OrderType = OrderType.Limit,
        Price = 50_000.04,
        Volume = 0.00005,
        OrderId = "test-order-id",
      }
    );

    Assert.IsTrue(accepted, "The stubbed success response should be reported as accepted.");
    Assert.IsNotNull(handler.Request, "No request reached the transport.");
    Assert.AreEqual(HttpMethod.Post, handler.Request.Method);
    Assert.AreEqual("/0/private/AddOrder", handler.Request.RequestUri!.AbsolutePath);
    Assert.AreEqual("https://api.kraken.com", handler.Request.RequestUri.GetLeftPart(UriPartial.Authority));

    var body = JsonNode.Parse(handler.Body!)!.AsObject();
    Assert.AreEqual("XBTCHF", (string?)body["pair"]);
    Assert.AreEqual("buy", (string?)body["type"]);
    Assert.AreEqual("limit", (string?)body["ordertype"]);
    Assert.AreEqual(0.00005, (double?)body["volume"]);
    Assert.AreEqual(50_000.0, (double?)body["price"], "Price is rounded to one decimal.");
    Assert.AreEqual("test-order-id", (string?)body["cl_ord_id"]);
    Assert.IsTrue(body.ContainsKey("nonce"), "The request must carry a nonce.");
  }

  [TestMethod]
  public async Task SendOrder_AuthenticatesWithAKeyAndAMatchingSignature()
  {
    var handler = new RecordingHandler(AddOrderResponse);
    var api = new KrakenApi(
      NullLogger<KrakenApi>.Instance,
      MsOptions.Create(new Secrets { ApiKey = ApiKey, ApiSecret = ApiSecret }),
      handler
    );
    using var client = new KrakenClient(NullLogger<KrakenClient>.Instance, api);

    await client.SendOrder(
      new OrderRequest
      {
        Pair = "XBTCHF",
        Type = BuyOrSell.Buy,
        OrderType = OrderType.Limit,
        Price = 50_000.0,
        Volume = 0.00005,
      }
    );

    var headers = handler.Request!.Content!.Headers;
    Assert.AreEqual(ApiKey, headers.GetValues("API-Key").Single());

    var nonce = (long)JsonNode.Parse(handler.Body!)!["nonce"]!;
    Assert.AreEqual(
      ExpectedSignature("/0/private/AddOrder", handler.Body!, nonce),
      headers.GetValues("API-Sign").Single(),
      "API-Sign must be the HMAC-SHA512 of the url path and the SHA-256 of nonce plus body."
    );
  }

  [TestMethod]
  public async Task GetCurrentCryptoPrice_QueriesThePublicTickerWithoutCredentials()
  {
    var handler = new RecordingHandler(TickerResponse);
    var api = new KrakenApi(
      NullLogger<KrakenApi>.Instance,
      MsOptions.Create(new Secrets()),
      handler
    );
    using var client = new KrakenClient(NullLogger<KrakenClient>.Instance, api);

    var price = await client.GetCurrentCryptoPrice("XBTCHF");

    Assert.AreEqual(HttpMethod.Get, handler.Request!.Method);
    Assert.AreEqual("/0/public/Ticker", handler.Request.RequestUri!.AbsolutePath);
    Assert.AreEqual("pair=XBTCHF", handler.Request.RequestUri.Query.TrimStart('?'));
    Assert.IsNull(handler.Request.Content, "A public query must not carry a signed body.");
    // Only that a price came back, not its value: the parse still runs under the current culture,
    // and pinning it to invariant culture is P2-02.
    Assert.AreNotEqual(0.0, price, "The ask price should have been parsed off the response.");
  }

  private static string ExpectedSignature(string urlPath, string jsonBody, long nonce)
  {
    var shaSum = SHA256.HashData(Encoding.UTF8.GetBytes(nonce.ToString() + jsonBody));
    var path = Encoding.UTF8.GetBytes(urlPath);
    using var hmac = new HMACSHA512(Convert.FromBase64String(ApiSecret));
    return Convert.ToBase64String(hmac.ComputeHash([.. path, .. shaSum]));
  }

  /// <summary>
  /// Captures the single request it is given and answers it from canned content.
  /// </summary>
  private sealed class RecordingHandler(string responseContent) : HttpMessageHandler
  {
    internal HttpRequestMessage? Request { get; private set; }
    internal string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      Request = request;
      Body =
        request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
      return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
      {
        Content = new StringContent(responseContent, Encoding.UTF8, "application/json"),
      };
    }
  }
}
