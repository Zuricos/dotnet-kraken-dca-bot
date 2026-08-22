using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kbot.Common.Dtos;
using Kbot.Common.Enums;
using Kbot.Common.Helpers;
using Kbot.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kbot.Common.Api;

public sealed class KrakenApi : IDisposable
{
  private static readonly Uri BaseAddress = new("https://api.kraken.com");

  private readonly ILogger<KrakenApi> _logger;
  private readonly IOptions<Secrets> _secrets;
  private readonly string _apiVersion = "0";
  private readonly HttpClient _httpClient;

  public KrakenApi(ILogger<KrakenApi> logger, IOptions<Secrets> secrets)
    : this(logger, secrets, new HttpClient { BaseAddress = BaseAddress }) { }

  /// <summary>
  /// Test seam: lets a stub <see cref="HttpMessageHandler"/> stand in for the real transport, so
  /// the shape of a signed request can be asserted without reaching Kraken. Replacing the
  /// hand-built <see cref="HttpClient"/> with IHttpClientFactory is tracked separately (P4-03).
  /// </summary>
  internal KrakenApi(
    ILogger<KrakenApi> logger,
    IOptions<Secrets> secrets,
    HttpMessageHandler handler
  )
    : this(logger, secrets, new HttpClient(handler) { BaseAddress = BaseAddress }) { }

  private KrakenApi(ILogger<KrakenApi> logger, IOptions<Secrets> secrets, HttpClient httpClient)
  {
    _logger = logger;
    _secrets = secrets;
    _httpClient = httpClient;
  }

  public void Dispose()
  {
    _httpClient.Dispose();
  }

  internal async Task<ApiResponse<T>?> GetPublicAsync<T>(
    PublicMethod method,
    Dictionary<string, string> parameters
  )
  {
    try
    {
      var url = $"/{_apiVersion}/public/{method}";
      if (parameters.Any())
      {
        url += "?" + ApiUtility.ToQueryString(parameters);
      }
      var response = await _httpClient.GetAsync(url);

      response.EnsureSuccessStatusCode();
      var content = await response.Content.ReadAsStringAsync();
      return JsonSerializer.Deserialize<ApiResponse<T>>(content);
    }
    catch (HttpRequestException e)
    {
      _logger.LogError("Request error occurred: {Message}", e.Message);
      return null;
    }
    catch (Exception e)
    {
      _logger.LogError("An unexpected error occurred: {Message}", e.Message);
      return null;
    }
  }

  internal async Task<ApiResponse<T>?> PostPrivateAsync<T>(
    PrivateMethod method,
    Dictionary<string, object> body
  )
  {
    try
    {
      var urlPath = $"/{_apiVersion}/private/{method}";
      var s = _secrets.Value.ApiKey;
      // Add nonce
      var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
      body.Add("nonce", nonce);
      var jsonBody = JsonSerializer.Serialize(body);
      // Create signature
      var signature = CreateSignature(urlPath, jsonBody, nonce);
      var response = await PostAsync(
        urlPath,
        new Dictionary<string, object>
        {
          { "API-Key", _secrets.Value.ApiKey },
          { "API-Sign", signature },
        },
        jsonBody
      );
      if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
      {
        _logger.LogWarning("Rate limit exceeded. Please try again later.");
        return null;
      }
      response.EnsureSuccessStatusCode();

      var content = await response.Content.ReadAsStringAsync();
      return JsonSerializer.Deserialize<ApiResponse<T>>(content);
    }
    catch (HttpRequestException e)
    {
      _logger.LogError("Request error occurred: {Message}", e.Message);
      return null;
    }
    catch (Exception e)
    {
      _logger.LogError("An unexpected error occurred in QueryPrivateAsync: {Message}", e.Message);
      return null;
    }
  }

  private Task<HttpResponseMessage> PostAsync(
    string url,
    Dictionary<string, object> headers,
    string jsonBody
  )
  {
    var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");
    foreach (var header in headers)
    {
      httpContent.Headers.Add(header.Key, header.Value.ToString());
    }

    return _httpClient.PostAsync(url, httpContent);
  }

  private string CreateSignature(string urlPath, string jsonBody, long nonce)
  {
    string encodedData;

    encodedData = nonce.ToString() + jsonBody;

    var shaSum = SHA256.HashData(Encoding.UTF8.GetBytes(encodedData));

    var message = Encoding.UTF8.GetBytes(urlPath);
    var combinedMessage = new byte[message.Length + shaSum.Length];
    Buffer.BlockCopy(message, 0, combinedMessage, 0, message.Length);
    Buffer.BlockCopy(shaSum, 0, combinedMessage, message.Length, shaSum.Length);

    byte[] secretBytes = Convert.FromBase64String(_secrets.Value.ApiSecret);
    using var hmac = new HMACSHA512(secretBytes);
    var macSum = hmac.ComputeHash(combinedMessage);
    return Convert.ToBase64String(macSum);
  }
}
