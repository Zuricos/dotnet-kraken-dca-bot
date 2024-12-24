using System.Net;

namespace Kbot.Common.Helpers;
public static class ApiUtility
{
    public static string ToQueryString(Dictionary<string, object> parameters)
    {
        var parametersAsString = new Dictionary<string, string>();
        foreach (var param in parameters)
        {
            parametersAsString.Add(param.Key, param.Value?.ToString() ?? "");
        }
        return ToQueryString(parametersAsString);
    }

    public static string ToQueryString(Dictionary<string, string> parameters)
    {
        List<string> encodedParams = new();
        foreach (var param in parameters)
        {
            var key = WebUtility.UrlEncode(param.Key);
            var value = WebUtility.UrlEncode(param.Value);
            encodedParams.Add($"{key}={value}");
        }
        return string.Join("&", encodedParams);
    }
}