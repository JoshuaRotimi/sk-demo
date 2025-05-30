using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;
using System.Text.Json;

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
    kernel.ImportPluginFromType<TransactionProcessorPlugin>();
    kernel.ImportPluginFromType<AccountManagerPlugin>();
    
    return kernel;
});

// Register services
builder.Services.AddSingleton<IBankingService, BankingService>();
builder.Services.AddSingleton<IUserAccountService, UserAccountService>();

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

app.Urls.Add("http://0.0.0.0:8080");
app.Run("http://0.0.0.0:5000");

// ===== MODELS =====
public class UserAccount
{
    public string UserId { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string BankName { get; set; } = "";
    public string FullName { get; set; } = "";
    public decimal Balance { get; set; }
    public List<TransactionRecord> TransactionHistory { get; set; } = new();
}

public class TransactionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ToBank { get; set; } = "";
    public string ToAccount { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Status { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> SecurityFlags { get; set; } = new();
}

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string UserId { get; set; } = "default_user"; // In real app, get from auth
    public string? SessionId { get; set; } // Added SessionId property
}

public class ChatResponse
{
    public string Response { get; set; } = "";
    public string? SessionId { get; set; } // Added SessionId property
    public TransactionRecord? ProcessedTransaction { get; set; }
    public List<string>? SecurityAlerts { get; set; }
}

// ===== CONTROLLER =====
[ApiController]
[Route("api/[controller]")]
public class BankingController : ControllerBase
{
    private readonly IBankingService _bankingService;

    public BankingController(IBankingService bankingService)
    {
        _bankingService = bankingService;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request)
    {
        try
        {
            var response = await _bankingService.ProcessChatAsync(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("account/{userId}")]
    public async Task<ActionResult<UserAccount>> GetAccount(string userId)
    {
        try
        {
            var account = await _bankingService.GetUserAccountAsync(userId);
            return Ok(account);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

// ===== SERVICES =====
public interface IBankingService
{
    Task<ChatResponse> ProcessChatAsync(ChatRequest request);
    Task<UserAccount> GetUserAccountAsync(string userId);
}

public interface IUserAccountService
{
    Task<UserAccount> GetUserAccountAsync(string userId);
    Task<bool> UpdateAccountAsync(UserAccount account);
}

public class BankingService : IBankingService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly IUserAccountService _accountService;
    private readonly OpenAIPromptExecutionSettings _executionSettings;
    private static readonly Dictionary<string, ChatHistory> _chatSessions = new();

    public BankingService(Kernel kernel, IUserAccountService accountService)
    {
        _kernel = kernel;
        _chatService = kernel.GetRequiredService<IChatCompletionService>();
        _accountService = accountService;
        _executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        };
    }

    public async Task<ChatResponse> ProcessChatAsync(ChatRequest request)
    {
        string sessionId = request.SessionId ?? Guid.NewGuid().ToString();

        if (!_chatSessions.ContainsKey(sessionId))
        {
            var history = new ChatHistory();
            
            // IMPROVED SYSTEM PROMPT - This is the key fix
            history.AddSystemMessage(@"You are a smart banking assistant for John Doe's Access Bank account (2089893421) with current balance ₦2,500,000.

BEHAVIOR:
- When user mentions transferring/sending money, IMMEDIATELY extract the details and process using the available plugins
- Parse recipient info from natural language (e.g., 'Ahmed Musa GTBank 3344558767' = GTBank, account 3344558767)
- Don't ask for information you can extract from their message
- Use plugins to run security checks automatically
- Be conversational and helpful, not robotic

EXAMPLES:
User: 'Send 60k to Ahmed Musa GTBank 3344558767'
You: 'I'll process your transfer of ₦60,000 to GTBank account 3344558767 for Ahmed Musa...' [then call TransactionClassifier and other plugins]

User: 'Transfer 500000 to Access Bank 1234567890'  
You: 'Processing ₦500,000 transfer to Access Bank account 1234567890...' [then use plugins]

Always run security checks but present results naturally in conversation.");

            _chatSessions[sessionId] = history;
        }

        var chatHistory = _chatSessions[sessionId];
        chatHistory.AddUserMessage(request.Message);

        try
        {
            var userAccount = await _accountService.GetUserAccountAsync(request.UserId);
            KernelProvider.SetKernel(_kernel);
            _kernel.Data["CurrentUser"] = userAccount;

            var reply = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings: _executionSettings,
                kernel: _kernel
            );

            chatHistory.AddAssistantMessage(reply.ToString());

            var chatResponse = new ChatResponse
            {
                Response = reply.ToString(),
                SessionId = sessionId
            };

            // Check if the response contains a transaction result
            if (reply.ToString().Contains("Transfer Successful") || reply.ToString().Contains("Transaction Blocked") || reply.ToString().Contains("Transaction Failed"))
            {
                chatResponse.Response = reply.ToString();
            }

            return chatResponse;
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

    public async Task<UserAccount> GetUserAccountAsync(string userId)
    {
        return await _accountService.GetUserAccountAsync(userId);
    }
}

public class UserAccountService : IUserAccountService
{
    // In-memory storage for demo - use database in production
    private static readonly Dictionary<string, UserAccount> _accounts = new()
    {
        ["default_user"] = new UserAccount
        {
            UserId = "default_user",
            AccountNumber = "2089893421",
            BankName = "Access Bank",
            FullName = "John Doe",
            Balance = 2500000,
            TransactionHistory = new List<TransactionRecord>
            {
                new() { ToBank = "GTBank", ToAccount = "0123456789", Amount = 25000, 
                       Timestamp = DateTime.Now.AddDays(-2), Status = "Completed" },
                new() { ToBank = "UBA", ToAccount = "1111111111", Amount = 100000, 
                       Timestamp = DateTime.Now.AddDays(-5), Status = "Completed" }
            }
        }
    };

    public async Task<UserAccount> GetUserAccountAsync(string userId)
    {
        await Task.Delay(1); // Simulate async
        return _accounts.GetValueOrDefault(userId) ?? throw new Exception("User not found");
    }

    public async Task<bool> UpdateAccountAsync(UserAccount account)
    {
        await Task.Delay(1);
        _accounts[account.UserId] = account;
        return true;
    }
}

// ===== SMART PLUGINS =====
public class TransactionProcessorPlugin
{
    [KernelFunction, Description("Process a money transfer with automatic security checks")]
    public async Task<string> ProcessTransfer(
        [Description("Recipient bank name (e.g., GTBank, Access Bank, UBA)")] string recipientBank,
        [Description("Recipient account number")] string recipientAccount,
        [Description("Transfer amount in Naira")] decimal amount,
        [Description("Optional description or purpose")] string description = "")
    {
        try
        {
            var kernel = KernelProvider.GetKernel();
            var currentUser = (UserAccount)kernel.Data["CurrentUser"];

            // Run all security checks
            var securityResults = new List<string>();
            var isBlocked = false;

            // 1. Classification check
            var classification = ClassifyTransaction(amount, currentUser.TransactionHistory);
            securityResults.Add($"Classification: {classification}");
            if (classification == "BLOCKED") isBlocked = true;

        // 2. Fraud detection
        var fraudCheck = CheckFraud(recipientBank, recipientAccount, amount);
        securityResults.Add($"Fraud Check: {fraudCheck}");
        if (fraudCheck.Contains("BLOCKED")) isBlocked = true;

        // 3. Balance check
        if (amount > currentUser.Balance)
        {
            securityResults.Add("Balance Check: INSUFFICIENT FUNDS");
            isBlocked = true;
        }
        else
        {
            securityResults.Add("Balance Check: SUFFICIENT");
        }

        // 4. Velocity check
        var velocityCheck = CheckVelocity(currentUser.TransactionHistory);
        securityResults.Add($"Velocity Check: {velocityCheck}");
            if (velocityCheck.Contains("EXCEEDED")) isBlocked = true;

            if (isBlocked)
            {
                return $"❌ **Transaction Blocked**\n\n" +
                       $"Transfer of ₦{amount:N0} to {recipientBank} ({recipientAccount}) cannot be processed.\n\n" +
                       $"**Security Checks:**\n{string.Join("\n", securityResults.Select(r => $"• {r}"))}";
            }

            // Process successful transaction
            var transaction = new TransactionRecord
            {
                ToBank = recipientBank,
                ToAccount = recipientAccount,
                Amount = amount,
                Status = "Completed",
                Description = description,
                SecurityFlags = securityResults
            };

            currentUser.TransactionHistory.Add(transaction);
            currentUser.Balance -= amount;

            var kycWarning = amount >= 500000 ? "\n\n⚠️ **KYC Notice:** This transaction may require additional verification due to the amount." : "";

            return $"✅ **Transfer Successful**\n\n" +
                   $"₦{amount:N0} sent to {recipientBank} account {recipientAccount}\n" +
                   $"New balance: ₦{currentUser.Balance:N0}\n" +
                   $"Transaction ID: {transaction.Id.Substring(0, 8)}" +
                   kycWarning;
        }
        catch (Exception ex)
        {
            return $"❌ **Transaction Failed**\n\n" +
                   $"An error occurred while processing the transaction: {ex.Message}";
        }
    }

    private string ClassifyTransaction(decimal amount, List<TransactionRecord> history)
    {
        if (amount >= 1000000) return "BLOCKED (Excessive amount)";

        var sameAmountCount = history.Count(t => t.Amount == amount &&
                                                    t.Timestamp > DateTime.Now.AddDays(-30));
        if (sameAmountCount >= 3) return "BLOCKED (Repeated amount pattern)";

        if (amount >= 500000) return "HIGH VALUE";
        return "NORMAL";
    }

    private string CheckFraud(string bank, string account, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(bank) || bank.ToLower().Contains("unknown"))
            return "BLOCKED (Invalid bank)";

        if (account.Length != 10 || !account.All(char.IsDigit))
            return "BLOCKED (Invalid account format)";

        if (amount % 1000 == 0 && amount > 50000)
            return "WARNING (Round amount pattern)";

        return "PASSED";
    }

    private string CheckVelocity(List<TransactionRecord> history)
    {
        var recentTransactions = history.Count(t => t.Timestamp > DateTime.Now.AddHours(-1));
        if (recentTransactions >= 5) return "EXCEEDED (Too many recent transactions)";
        return $"PASSED ({recentTransactions}/5 hourly limit)";
    }
}

public class AccountManagerPlugin
{
    [KernelFunction, Description("Get current account balance and recent transactions")]
    public async Task<string> GetAccountSummary()
    {
        try
        {
            await Task.Delay(1);
            var kernel = KernelProvider.GetKernel();
            var currentUser = (UserAccount)kernel.Data["CurrentUser"];

            var recentTransactions = currentUser.TransactionHistory
                .OrderByDescending(t => t.Timestamp)
                .Take(3)
                .ToList();

            var summary = $"**Account Summary for {currentUser.FullName}**\n\n" +
                         $"💰 Current Balance: ₦{currentUser.Balance:N0}\n" +
                         $"🏦 Bank: {currentUser.BankName}\n" +
                         $"📱 Account: {currentUser.AccountNumber}\n\n";

            if (recentTransactions.Any())
            {
                summary += "**Recent Transactions:**\n";
                foreach (var tx in recentTransactions)
                {
                    summary += $"• ₦{tx.Amount:N0} to {tx.ToBank} - {tx.Timestamp:MMM dd}\n";
                }
            }

            return summary;
        }
        catch (Exception ex)
        {
            return $"❌ **Account Summary Failed**\n\n" +
                   $"An error occurred while retrieving the account summary: {ex.Message}";
        }
    }
}

// Helper class to access kernel in plugins
public static class KernelProvider
{
    private static Kernel? _kernel;
    
    public static void SetKernel(Kernel kernel) => _kernel = kernel;
    public static Kernel GetKernel() => _kernel ?? throw new InvalidOperationException("Kernel not set");
}
