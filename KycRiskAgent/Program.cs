 using Microsoft.SemanticKernel;
 using Microsoft.SemanticKernel.ChatCompletion;
 using Microsoft.SemanticKernel.Connectors.OpenAI;
 using Plugins.KYCPlugin;



 using Microsoft.Extensions.Configuration;

string filePath = Path.GetFullPath("appsettings.json");
var config = new ConfigurationBuilder()
.AddJsonFile(filePath)
.Build();

// Set your values in appsettings.json
string modelId = config["modelId"]!;
string endpoint = config["endpoint"]!;
string apiKey = config["apiKey"]!;

// Create a kernel builder with Azure OpenAI chat completion
 var builder = Kernel.CreateBuilder();
 builder.AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);

  // Build the kernel
 var kernel = builder.Build();

 kernel.ImportPluginFromObject(new KYCPlugin(), "KYC");

// Load prompt-based plugin
kernel.ImportPluginFromPromptDirectory("plugins/KYCPlugin/RiskAssessment", "RiskAssessment");

  // Verify the endpoint and run a prompt
var result = await kernel.InvokePromptAsync("Who are the top 5 most famous musicians in the world?");
 Console.WriteLine(result);