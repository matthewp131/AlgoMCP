var builder = WebApplication.CreateBuilder(args);

// load secrets
builder.Configuration.AddUserSecrets<Program>();
var config = builder.Configuration;
String API_KEY = config["API_KEY"] ?? throw new ArgumentNullException("API_KEY is not configured in user secrets.");
String API_SECRET = config["API_SECRET"] ?? throw new ArgumentNullException("API_SECRET is not configured in user secrets.");

builder.Services.AddControllers();

builder.Services.AddOpenApi();

builder.Services.AddSingleton<AlgoMCPServer.Services.UserService>();

var cts = new CancellationTokenSource();
builder.Services.AddSingleton<AlgoMCPServer.Services.StrategyService>(sp =>
{
    var userService = sp.GetRequiredService<AlgoMCPServer.Services.UserService>();
    return new AlgoMCPServer.Services.StrategyService(userService, cts);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// temporary startup code
var userService = app.Services.GetRequiredService<AlgoMCPServer.Services.UserService>();
var strategyService = app.Services.GetRequiredService<AlgoMCPServer.Services.StrategyService>();
string username = "defaultUser"; // Example username

if (userService.AddUser(username, API_KEY, API_SECRET))
{
    Console.WriteLine($"User '{username}' added.");

    // Example: Initialize strategy for the user
    string symbol = "DOGE/USD";
    // Renamed variable
    decimal accountAllocationPercent = 0.25m; // Use 25% of account equity/buying power for this strategy

    strategyService.InitializeStrategyAsync(username, symbol, accountAllocationPercent);
}
else
{
    Console.WriteLine($"Failed to add user '{username}'.");
}

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("Application is shutting down. Shutting down all strategies...");
    strategyService.StopAllStrategies().GetAwaiter().GetResult();
});

app.Run();

// await app.RunAsync(cts.Token);
