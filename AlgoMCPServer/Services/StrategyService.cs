namespace AlgoMCPServer.Services {
    using System.Collections.Concurrent;
    using System.Threading.Tasks;
    using AlgoMCPServer.Strategies;

    readonly record struct StrategyInfo(IStrategy Strategy, CancellationTokenSource Cts, decimal Allocation, Task Task);

    public class StrategyService(UserService userService, CancellationTokenSource cts) {
        private readonly UserService _userService = userService;
        private readonly CancellationTokenSource _cts = cts;
        private readonly ConcurrentDictionary<string, List<StrategyInfo>> activeStrategiesByUser = new();

        public void InitializeStrategyAsync(string username, string symbol, decimal accountAllocation) {
            var user = _userService.GetUser(username);
            if (user == null) {
                Console.WriteLine($"User '{username}' not found.");
                return;
            }

            if (accountAllocation <= 0 || accountAllocation > 1) {
                Console.WriteLine("Account allocation percentage must be between 0 (exclusive) and 1 (inclusive).");
                return;
            }

            // TODO - need to make this thread-safe
            if (!_userService.TryAllocate(username, accountAllocation)) {
                return;
            }

            var cts = new CancellationTokenSource();
            var strategy = new MeanReversionWithCrypto(user.ApiKey, user.ApiSecret, symbol, accountAllocation, cts.Token);

            Console.WriteLine($"Starting strategy for user '{username}' with symbol '{symbol}' and {accountAllocation:P} account allocation.");
            // Run the strategy asynchronously
            var task = Task.Run(async () => {
                decimal allocationToReturn = accountAllocation; // Capture allocation for finally block
                try {
                    await strategy.Run();
                    Console.WriteLine($"Strategy for user '{username}' completed.");
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"Error running strategy for user '{username}': {ex}");
                }
                finally {
                    strategy.Dispose();
                    // Remove strategy first
                    activeStrategiesByUser.TryRemove(username, out _);
                    // Then deallocate
                    _userService.Deallocate(username, allocationToReturn);
                    cts.Dispose();
                }
            });

            var strategies_for_user = activeStrategiesByUser.GetOrAdd(username, _ => new List<StrategyInfo>());

            lock (strategies_for_user) {
                strategies_for_user.Add(new StrategyInfo(strategy, cts, accountAllocation, task));
            }
        }

        public async Task StopStrategy(string username) {
            if (activeStrategiesByUser.TryGetValue(username, out var activeStrategies)) {
                foreach (var strategyInfo in activeStrategies) {
                    Console.WriteLine($"Stopping strategy for user '{username}'.");
                    strategyInfo.Cts.Cancel();
                    await strategyInfo.Task;
                }
            } else {
                Console.WriteLine($"No active strategies found for user '{username}'.");
            }
        }

        public async Task StopAllStrategies() {
            Console.WriteLine("Stopping all active strategies...");
            foreach (var username in activeStrategiesByUser.Keys.ToList()) // Use ToList to avoid modification during iteration
            {
                await StopStrategy(username);
            }
            _cts.Cancel();
        }
    }

}
