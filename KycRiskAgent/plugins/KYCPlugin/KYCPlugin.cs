using System.ComponentModel;
using Microsoft.SemanticKernel;
using System.Threading.Tasks;

namespace Plugins.KYCPlugin
{
    public class KYCPlugin
    {
        [KernelFunction, Description("Validates a customer's national ID number format.")]
        public string ValidateNationalID(
            [Description("The national ID number")] string nationalId)
        {
            // Dummy validation logic
            return nationalId.Length == 11 ? "Valid ID" : "Invalid ID";
        }

        [KernelFunction, Description("Checks if a customer exists in the fraud database.")]
        public async Task<string> CheckFraudDatabaseAsync(
            [Description("Customer full name")] string fullName)
        {
            // Simulated API or database check
            var flaggedNames = new[] { "John Doe", "Jane Fraud" };
            await Task.Delay(100); // Simulate latency
            return flaggedNames.Contains(fullName) ? "Flagged" : "Clear";
        }
    }
}
