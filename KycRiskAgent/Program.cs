using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Plugins.KYCPlugin;

string filePath = Path.GetFullPath("appsettings.json");
var config = new ConfigurationBuilder().AddJsonFile(filePath).Build();

string modelId = config["modelId"]!;
string endpoint = config["endpoint"]!;
string apiKey = config["apiKey"]!;

var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);
var kernel = builder.Build();

// Load plugin
var kycPlugin = kernel.CreatePluginFromPromptDirectory("Plugins/KycRiskPlugin");

// Execution settings
OpenAIPromptExecutionSettings settings = new()
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

Console.WriteLine("KYC Risk Agent Ready\n");

while (true)
{
    Console.Write("Enter customer name (or leave blank to exit): ");
    string input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) break;

    var docResult = await kernel.InvokeAsync(kycPlugin["validate_id_document"], new() { ["userInput"] = "Valid national ID with matching address" });
    var sanctionsResult = await kernel.InvokeAsync(kycPlugin["screen_against_sanctions"], new() { ["userInput"] = input });
    var behaviorResult = await kernel.InvokeAsync(kycPlugin["assess_behavioral_risk"], new() { ["userInput"] = "Normal login pattern, single region access" });

    var finalResult = await kernel.InvokeAsync(kycPlugin["combine_risk_signals"], new()
    {
        ["docRisk"] = docResult.ToString(),
        ["sanctionsRisk"] = sanctionsResult.ToString(),
        ["behaviorRisk"] = behaviorResult.ToString()
    });

    Console.WriteLine("\nKYC Summary:");
    Console.WriteLine($"📝 Document Check: {docResult}");
    Console.WriteLine($"📋 Sanctions Check: {sanctionsResult}");
    Console.WriteLine($"📊 Behavior Risk: {behaviorResult}");
    Console.WriteLine($"➡ Final Verdict: {finalResult}\n");
}

