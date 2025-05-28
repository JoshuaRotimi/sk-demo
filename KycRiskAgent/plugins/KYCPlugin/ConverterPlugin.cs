using System.ComponentModel;
using Microsoft.SemanticKernel;

public class CurrencyConverterPlugin
{
    // A dictionary that stores exchange rates for demonstration
    private static Dictionary<string, decimal> exchangeRates = new Dictionary<string, decimal>
    {
        { "USD-EUR", 0.85m },
        { "EUR-USD", 1.18m },
        { "USD-GBP", 0.75m },
        { "GBP-USD", 1.33m },
        { "USD-JPY", 110.50m },
        { "JPY-USD", 1 / 110.50m },
        { "USD-HKD", 7.77m },
        { "HKD-USD", 1 / 7.77m }
    };

    // Get the exchange rate from one currency to another
    public static decimal GetExchangeRate(string fromCurrency, string toCurrency)
    {
        string key = $"{fromCurrency}-{toCurrency}";
        if (exchangeRates.ContainsKey(key))
        {
            return exchangeRates[key];
        }
        else
        {
            throw new Exception($"Exchange rate not available for {fromCurrency}-{toCurrency} currency pair.");
        }
    }

    // Create a kernel function that gets the exchange rate
    [KernelFunction("convert_currency")]
    [Description("Converts an amount from one currency to another. Use currency codes like USD, EUR, GBP, JPY, HKD. For example, convert 10 USD to HKD.")]
    public static decimal ConvertCurrency(
        [Description("The amount to convert")] decimal amount, 
        [Description("The source currency code (e.g., USD, EUR)")] string fromCurrency, 
        [Description("The target currency code (e.g., HKD, GBP)")] string toCurrency)
    {
        decimal exchangeRate = GetExchangeRate(fromCurrency.ToUpper(), toCurrency.ToUpper());
        return amount * exchangeRate;
    }
}