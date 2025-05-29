using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Semantic Kernel
string modelId = builder.Configuration["OpenAI:ModelId"];
string endpoint = builder.Configuration["OpenAI:Endpoint"];
string apiKey = builder.Configuration["OpenAI:ApiKey"];

// Register Semantic Kernel as a singleton
builder.Services.AddSingleton<Kernel>(serviceProvider =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);
    var kernel = kernelBuilder.Build();
    
    // Add all plugins
    kernel.ImportPluginFromType<TransactionClassifierPlugin>();
    kernel.ImportPluginFromType<FraudDetectionPlugin>();
    kernel.ImportPluginFromType<KYCPlugin>();
    kernel.ImportPluginFromType<VelocityCheckPlugin>();
    
    return kernel;
});

// Register transaction service
builder.Services.AddSingleton<ITransactionSecurityService, TransactionSecurityService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Configure for Codespaces
app.Run("http://0.0.0.0:5000");

// ===== API MODELS =====
public class TransactionRequest
{
    public string Bank { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string UserMessage { get; set; } = "";
}

public class TransactionResponse
{
    public bool IsAllowed { get; set; }
    public string Classification { get; set; } = "";
    public List<SecurityCheckResult> SecurityChecks { get; set; } = new();
    public string Message { get; set; } = "";
    public string AssistantResponse { get; set; } = "";
}

public class SecurityCheckResult
{
    public string CheckName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
    public bool IsBlocking { get; set; }
}

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string? SessionId { get; set; }
}

public class ChatResponse
{
    public string Response { get; set; } = "";
    public string SessionId { get; set; } = "";
    public List<SecurityCheckResult>? SecurityChecks { get; set; }
}

// ===== API CONTROLLER =====
[ApiController]
[Route("api/[controller]")]
public class TransactionController : ControllerBase
{
    private readonly ITransactionSecurityService _transactionService;

    public TransactionController(ITransactionSecurityService transactionService)
    {
        _transactionService = transactionService;
    }

    [HttpPost("validate")]
    public async Task<ActionResult<TransactionResponse>> ValidateTransaction([FromBody] TransactionRequest request)
    {
        try
        {
            var response = await _transactionService.ValidateTransactionAsync(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request)
    {
        try
        {
            var response = await _transactionService.ProcessChatAsync(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}

// ===== SERVICE LAYER =====
public interface ITransactionSecurityService
{
    Task<TransactionResponse> ValidateTransactionAsync(TransactionRequest request);
    Task<ChatResponse> ProcessChatAsync(ChatRequest request);
}

public class TransactionSecurityService : ITransactionSecurityService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly OpenAIPromptExecutionSettings _executionSettings;
    private static readonly Dictionary<string, ChatHistory> _chatSessions = new();

    public TransactionSecurityService(Kernel kernel)
    {
        _kernel = kernel;
        _chatService = kernel.GetRequiredService<IChatCompletionService>();
        _executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        };
    }

    public async Task<TransactionResponse> ValidateTransactionAsync(TransactionRequest request)
    {
        var response = new TransactionResponse();
        var securityChecks = new List<SecurityCheckResult>();

        try
        {
            // Run all security checks using direct plugin method calls
            var classifierPlugin = new TransactionClassifierPlugin();
            var classificationResult = await classifierPlugin.ClassifyTransaction(
                request.Bank, request.AccountNumber, request.Amount);
            securityChecks.Add(new SecurityCheckResult
            {
                CheckName = "TransactionClassifier",
                Status = classificationResult.Contains("BLOCKED") ? "BLOCKED" : "PASSED",
                Details = classificationResult,
                IsBlocking = classificationResult.Contains("BLOCKED")
            });

            var fraudPlugin = new FraudDetectionPlugin();
            var fraudResult = fraudPlugin.CheckFraudPatterns(
                request.Bank, request.AccountNumber, request.Amount);
            securityChecks.Add(new SecurityCheckResult
            {
                CheckName = "FraudDetection",
                Status = fraudResult.Contains("ALERT") ? "BLOCKED" : "PASSED",
                Details = fraudResult,
                IsBlocking = fraudResult.Contains("ALERT")
            });

            var kycPlugin = new KYCPlugin();
            var kycResult = kycPlugin.PerformKYCCheck(request.Amount, request.AccountNumber);
            securityChecks.Add(new SecurityCheckResult
            {
                CheckName = "KYC",
                Status = kycResult.Contains("REQUIRED") ? "WARNING" : "PASSED",
                Details = kycResult,
                IsBlocking = false // KYC is informational, not blocking
            });

            var velocityPlugin = new VelocityCheckPlugin();
            var velocityResult = velocityPlugin.CheckTransactionVelocity(
                request.AccountNumber, request.Amount);
            securityChecks.Add(new SecurityCheckResult
            {
                CheckName = "VelocityCheck",
                Status = velocityResult.Contains("EXCEEDED") ? "BLOCKED" : "PASSED",
                Details = velocityResult,
                IsBlocking = velocityResult.Contains("EXCEEDED")
            });

            // Determine overall result
            bool hasBlockingIssues = securityChecks.Any(c => c.IsBlocking);
            string classification = ExtractClassification(classificationResult);

            response.IsAllowed = !hasBlockingIssues && classification != "Abnormal";
            response.Classification = classification;
            response.SecurityChecks = securityChecks;
            response.Message = response.IsAllowed 
                ? $"Transaction approved: ₦{request.Amount:N0} to {request.Bank}"
                : "Transaction blocked due to security concerns";

            return response;
        }
        catch (Exception ex)
        {
            response.IsAllowed = false;
            response.Message = $"Error during validation: {ex.Message}";
            return response;
        }
    }

    public async Task<ChatResponse> ProcessChatAsync(ChatRequest request)
    {
        string sessionId = request.SessionId ?? Guid.NewGuid().ToString();
        
        if (!_chatSessions.ContainsKey(sessionId))
        {
            var history = new ChatHistory();
            history.AddSystemMessage("You are a banking security assistant. Help users process transactions safely by running appropriate security checks. Ask for transaction details when needed: bank, account number, amount, and any other relevant information.");
            _chatSessions[sessionId] = history;
        }

        var chatHistory = _chatSessions[sessionId];
        chatHistory.AddUserMessage(request.Message);

        try
        {
            var reply = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings: _executionSettings,
                kernel: _kernel
            );

            chatHistory.AddAssistantMessage(reply.ToString());

            return new ChatResponse
            {
                Response = reply.ToString(),
                SessionId = sessionId
            };
        }
        catch (Exception ex)
        {
            return new ChatResponse
            {
                Response = $"I apologize, but I encountered an error: {ex.Message}",
                SessionId = sessionId
            };
        }
    }

    private string ExtractClassification(string details)
    {
        if (details.Contains("Abnormal")) return "Abnormal";
        if (details.Contains("New")) return "New";
        return "Normal";
    }
}

// ===== PLUGIN CLASSES =====
public class TransactionRecord
{
    public string Bank { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
    public string Classification { get; set; } = "";
    public bool IsBlocked { get; set; }
}

public class TransactionClassifierPlugin
{
    private static readonly List<TransactionRecord> _history = new();

    [KernelFunction, Description("Classify a transaction as Normal, New, or Abnormal based on amount and history")]
    public async Task<string> ClassifyTransaction(
        [Description("Bank name")] string bank,
        [Description("Account number")] string accountNumber,
        [Description("Transaction amount in Naira")] decimal amount)
    {
        await Task.Delay(1); // Make it async
        
        int sameAmountCount = _history.Count(t => t.Amount == amount);
        
        string classification;
        if (amount >= 1000000 || sameAmountCount >= 3)
        {
            classification = "Abnormal";
        }
        else if (amount >= 500000)
        {
            classification = "New";
        }
        else
        {
            classification = "Normal";
        }

        var transaction = new TransactionRecord
        {
            Bank = bank,
            AccountNumber = accountNumber,
            Amount = amount,
            Timestamp = DateTime.Now,
            Classification = classification,
            IsBlocked = classification == "Abnormal"
        };
        
        if (classification != "Abnormal")
        {
            _history.Add(transaction);
        }

        return $"Transaction classified as: {classification}. " +
               (classification == "Abnormal" ? "BLOCKED" : "ALLOWED") +
               $" - ₦{amount:N0} to {bank} ({accountNumber})";
    }
}

public class FraudDetectionPlugin
{
    [KernelFunction, Description("Check for potential fraud patterns in transaction")]
    public string CheckFraudPatterns(
        [Description("Bank name")] string bank,
        [Description("Account number")] string accountNumber,
        [Description("Transaction amount")] decimal amount)
    {
        var riskFactors = new List<string>();
        
        if (amount % 1000 == 0 && amount > 50000)
            riskFactors.Add("Round number large amount");
        
        if (bank.ToLower().Contains("unknown") || string.IsNullOrEmpty(bank))
            riskFactors.Add("Unknown bank");
            
        if (accountNumber.Length != 10)
            riskFactors.Add("Invalid account number format");

        if (riskFactors.Any())
            return $"FRAUD ALERT: {string.Join(", ", riskFactors)}";
        
        return "No fraud patterns detected";
    }
}

public class KYCPlugin
{
    [KernelFunction, Description("Perform KYC verification for high-value transactions")]
    public string PerformKYCCheck(
        [Description("Transaction amount")] decimal amount,
        [Description("Account number")] string accountNumber)
    {
        if (amount >= 500000)
        {
            return $"KYC REQUIRED: Transaction amount ₦{amount:N0} requires identity verification. " +
                   "Please provide additional documentation.";
        }
        
        return "KYC verification not required for this transaction amount";
    }
}

public class VelocityCheckPlugin
{
    private static readonly Dictionary<string, List<DateTime>> _accountActivity = new();

    [KernelFunction, Description("Check transaction velocity limits")]
    public string CheckTransactionVelocity(
        [Description("Account number")] string accountNumber,
        [Description("Transaction amount")] decimal amount)
    {
        if (!_accountActivity.ContainsKey(accountNumber))
            _accountActivity[accountNumber] = new List<DateTime>();

        var recentTransactions = _accountActivity[accountNumber]
            .Where(t => t > DateTime.Now.AddHours(-1))
            .ToList();

        if (recentTransactions.Count >= 5)
        {
            return "VELOCITY LIMIT EXCEEDED: Too many transactions in the last hour. Please wait.";
        }

        _accountActivity[accountNumber].Add(DateTime.Now);
        return $"Velocity check passed. {recentTransactions.Count + 1} transactions in last hour.";
    }
}