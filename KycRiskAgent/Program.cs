using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;

string filePath = Path.GetFullPath("appsettings.json");
var config = new ConfigurationBuilder()
    .AddJsonFile(filePath)
    .Build();

// Set your values in appsettings.json
string modelId = config["modelId"]!;
string endpoint = config["endpoint"]!;
string apiKey = config["apiKey"]!;

// Create a kernel with Azure OpenAI chat completion
var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);
Kernel kernel = builder.Build();

// Add plugins to the kernel
kernel.ImportPluginFromType<CurrencyConverterPlugin>("CurrencyConverter");
// kernel.ImportPluginFromType<KYCPlugin>("KYC"); // Uncomment when you have KYCPlugin implemented

var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
};

// Setup the assistant chat
var history = new ChatHistory();

Console.WriteLine("Press enter to exit");
Console.WriteLine("Assistant: How may I help you?");
Console.Write("User: ");
string input = Console.ReadLine()!;

while (input != "")
{
    history.AddUserMessage(input);
    await GetReply();
    input = GetInput();
}

string GetInput() 
{
    Console.Write("User: ");
    string input = Console.ReadLine()!;
    // Don't add to history here - it's handled in the main loop
    return input;
}

async Task GetReply() 
{
    ChatMessageContent reply = await chatCompletionService.GetChatMessageContentAsync(
        history,
        executionSettings: openAIPromptExecutionSettings,
        kernel: kernel
    );
    Console.WriteLine("Assistant: " + reply.ToString());
    history.AddAssistantMessage(reply.ToString());
}