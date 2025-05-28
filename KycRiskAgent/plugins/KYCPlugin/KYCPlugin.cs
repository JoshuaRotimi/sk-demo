using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace Plugins.KYCPlugin
{
    public class KYCPlugin
    {
        private List<decimal> transactionHistory = new() { 100, 105, 110, 120, 130, 200 }; // Example

        [KernelFunction]
        [Description("Check if a transaction amount is an anomaly")]
        public string IsTransactionAmountAnomalous(string inputAmount)
        {
            if (!decimal.TryParse(inputAmount, out var currentAmount))
                return "Invalid input";

            var avg = transactionHistory.Average();
            var stdDev = Math.Sqrt(transactionHistory.Select(x => Math.Pow((double)(x - avg), 2)).Sum() / transactionHistory.Count);

            bool isAnomalous = Math.Abs(currentAmount - (decimal)avg) > (decimal)(2 * stdDev);
            return isAnomalous ? "Yes, the amount is anomalous." : "No, the amount is within expected range.";
        }

        [KernelFunction]
        [Description("Check if a transaction is too frequent")]
        public string IsFrequentTransfer(string timestampsJson)
        {
            try
            {
                var timestamps = JsonSerializer.Deserialize<List<DateTime>>(timestampsJson);
                timestamps.Sort();

                for (int i = 0; i < timestamps.Count - 2; i++)
                {
                    if ((timestamps[i + 2] - timestamps[i]).TotalMinutes <= 5)
                        return "Yes, 3 transfers within 5 minutes.";
                }

                return "No, transfer frequency is normal.";
            }
            catch
            {
                return "Invalid input format. Expecting a JSON array of timestamps.";
            }
        }
    }
}
