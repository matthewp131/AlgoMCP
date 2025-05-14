var builder = WebApplication.CreateBuilder(args);

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

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("Application is shutting down. Shutting down all strategies...");
    var strategyService = app.Services.GetRequiredService<AlgoMCPServer.Services.StrategyService>();
    strategyService.StopAllStrategies().GetAwaiter().GetResult();
});

app.Run();
