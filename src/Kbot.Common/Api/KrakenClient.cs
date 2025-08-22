using System.Diagnostics.CodeAnalysis;
using Kbot.Common.Dtos;
using Kbot.Common.Enums;
using Microsoft.Extensions.Logging;

namespace Kbot.Common.Api;

public sealed class KrakenClient(ILogger<KrakenClient> logger, KrakenApi api) : IDisposable
{
  /// <summary>
  /// Retrieves the current Balance from the Kraken API.
  /// </summary>
  /// <returns></returns>
  public async Task<Dictionary<string, double>> CheckBalance()
  {
    try
    {
      var response = await api.PostPrivateAsync<Dictionary<string, string>>(
        PrivateMethod.Balance,
        new Dictionary<string, object>()
      );
      if (HasError(response))
        return [];

      logger.LogInformation(
        "Current balance: {Balance}",
        string.Join(", ", response.Result!.Select(kvp => $"{kvp.Key}: {kvp.Value}"))
      );

      var balance = response.Result!.ToDictionary(x => x.Key, x => double.Parse(x.Value));
      return balance;
    }
    catch (Exception e)
    {
      logger.LogError("Could not query the balance: {Error}", e.Message);
      return [];
    }
  }

  /// <summary>
  /// Retrieves the current price of the given pair.
  /// </summary>
  public async Task<double> GetCurrentCryptoPrice(string pair = "XXBTZUSD")
  {
    try
    {
      var response = await api.GetPublicAsync<Dictionary<string, TickerInfoUnparsed>>(
        PublicMethod.Ticker,
        new Dictionary<string, string> { { "pair", pair } }
      );
      if (HasError(response))
        return 0.0;
      TickerInfo tickerInfo;
      tickerInfo = response.Result![pair].Parse();

      logger.LogInformation(
        "Current pair {Pair} price is: {CurrentPrice}",
        pair,
        tickerInfo.Ask.Price
      );
      return tickerInfo.Ask.Price;
    }
    catch (Exception e)
    {
      logger.LogError("Could not query the current price: {Error}", e.Message);
      return 0.0;
    }
  }

  public async Task<bool> SendOrder(OrderRequest orderData)
  {
    ApiResponse<OrderResponse>? response;
    try
    {
      response = await api.PostPrivateAsync<OrderResponse>(
        PrivateMethod.AddOrder,
        orderData.ToDictionary()
      );
      if (HasError(response))
        return false;
    }
    catch (Exception e)
    {
      logger.LogError("Could not send order: {Error}", e.Message);
      return false;
    }
    logger.LogInformation(
      "Order sent: {Order} for {Descr}",
      response.Result!.TransactionIds,
      response.Result!.Description.Order
    );
    return true;
  }

  /// <summary>
  /// Cancels the order with the given id. cl_ord_id is taken over orderId if both are given.
  /// </summary>
  /// <param name="cl_ord_id"></param>
  /// <param name="orderId"></param>
  /// <returns></returns>
  public async Task<bool> CancelOrder(string? cl_ord_id, string? orderId)
  {
    if (cl_ord_id == null && orderId == null)
    {
      logger.LogError("No order id given.");
      return false;
    }
    var orderData = new Dictionary<string, object>();
    if (cl_ord_id != null)
    {
      orderData.Add("cl_ord_id", cl_ord_id);
    }
    else
    {
      orderData.Add("txid", orderId!);
    }

    try
    {
      var response = await api.PostPrivateAsync<CancelOrderResponse>(
        PrivateMethod.CancelOrder,
        orderData
      );
      if (HasError(response))
        return false;
      return response.Result!.Count > 0;
    }
    catch (Exception e)
    {
      logger.LogError("Could not cancel order: {Error}", e.Message);
      return false;
    }
  }

  public async Task<ClosedOrders?> GetClosedOrders(
    bool fetchAll = true,
    int offset = 0,
    DateTimeOffset start = default,
    DateTimeOffset end = default
  )
  {
    var orderData = new Dictionary<string, object> { { "ofs", offset } };
    if (start != default)
    {
      orderData.Add("start", start.ToUnixTimeSeconds());
    }
    if (end != default)
    {
      orderData.Add("end", end.ToUnixTimeSeconds());
    }

    ApiResponse<ClosedOrderUnparsed>? response;
    try
    {
      response = await api.PostPrivateAsync<ClosedOrderUnparsed>(
        PrivateMethod.ClosedOrders,
        orderData
      );
      if (HasError(response))
        return null;
    }
    catch (Exception e)
    {
      logger.LogError("Could not query the closed orders: {Error}", e.Message);
      return null;
    }
    try
    {
      var closedOrders = response.Result!.ToModel();
      logger.LogInformation(
        "In the given time frame are {Count} closed orders",
        closedOrders.Closed.Count
      );
      logger.LogInformation("Retrieved {Count} closed orders.", closedOrders.Closed.Count);
      offset += 50;
      if (fetchAll && closedOrders.Count >= 50 && offset < closedOrders.Count)
      {
        Task.Delay(2000).Wait(); // Kraken API History limit is 1 request per 2 seconds
        var next = await GetClosedOrders(fetchAll: true, offset: offset, start: start, end: end);
        if (next != null)
        {
          closedOrders.Closed.AddRange(next.Closed);
        }
      }
      return closedOrders;
    }
    catch (Exception e)
    {
      logger.LogError("Could not parse ClosedOrderUnparsed to ClosedOrderInfo: {Error}", e.Message);
      return null;
    }
  }

  private bool HasError<T>([NotNull] ApiResponse<T>? response)
  {
    if (response == null)
    {
      logger.LogError("Reponse is null, probabily a parsing error.");
      throw new ArgumentNullException(nameof(response));
    }
    if (response.Error.Count > 0)
    {
      foreach (var error in response.Error)
      {
        logger.LogError("Could not query the balance: {Error}", error);
      }
      return true;
    }
    if (response.Result == null)
    {
      logger.LogError("Response result is null.");
      return true;
    }
    return false;
  }

  public void Dispose()
  {
    api.Dispose();
  }
}
