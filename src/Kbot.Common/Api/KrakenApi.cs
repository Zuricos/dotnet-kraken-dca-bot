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
public sealed class KrakenApi(ILogger<KrakenApi> logger, IOptions<Secrets> secrets) : IDisposable
{
    private readonly string _apiVersion = "0";
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri("https://api.kraken.com") };

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    internal async Task<ApiResponse<T>?> GetPublicAsync<T>(PublicMethod method, Dictionary<string, string> parameters)
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
            logger.LogError("Request error occurred: {Message}", e.Message);
            return null;
        }
        catch (Exception e)
        {
            logger.LogError("An unexpected error occurred: {Message}", e.Message);
            return null;
        }
    }

    internal async Task<ApiResponse<T>?> PostPrivateAsync<T>(PrivateMethod method, Dictionary<string, object> body)
    {
        try
        {
            var urlPath = $"/{_apiVersion}/private/{method}";
            var s = secrets.Value.ApiKey;
            // Add nonce
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            body.Add("nonce", nonce);
            var jsonBody = JsonSerializer.Serialize(body);
            // Create signature
            var signature = CreateSignature(urlPath, jsonBody, nonce);
            var response = await PostAsync(urlPath, new Dictionary<string, object>
            {
                { "API-Key", secrets.Value.ApiKey },
                { "API-Sign", signature }
            }, jsonBody);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Rate limit exceeded. Please try again later.");
                return null;
            }
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ApiResponse<T>>(content);
        }
        catch (HttpRequestException e)
        {
            logger.LogError("Request error occurred: {Message}", e.Message);
            return null;
        }
        catch (Exception e)
        {
            logger.LogError("An unexpected error occurred in QueryPrivateAsync: {Message}", e.Message);
            return null;
        }
    }

    private Task<HttpResponseMessage> PostAsync(string url, Dictionary<string, object> headers, string jsonBody)
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

        byte[] secretBytes = Convert.FromBase64String(secrets.Value.ApiSecret);
        using var hmac = new HMACSHA512(secretBytes);
        var macSum = hmac.ComputeHash(combinedMessage);
        return Convert.ToBase64String(macSum);
    }
}