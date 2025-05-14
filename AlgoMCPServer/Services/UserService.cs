namespace AlgoMCPServer.Services
{
    using System;
    using System.Collections.Concurrent;

    public record User(string Username, string ApiKey, string ApiSecret, decimal AvailableAllocation = 1.0m);

    public class UserService
    {
        private readonly ConcurrentDictionary<string, User> _users = new();

        public void AddUser(string username, string apiKey, string apiSecret)
        {
            var user = new User(username, apiKey, apiSecret);
            _users.AddOrUpdate(username, user, (_, _) => user);
        }

        public User? GetUser(string username)
        {
            _users.TryGetValue(username, out var user);
            return user;
        }

        public bool TryAllocate(string username, decimal amountToAllocate)
        {
            if (_users.TryGetValue(username, out var currentUser))
            {
                if (currentUser.AvailableAllocation >= amountToAllocate)
                {
                    var newUser = currentUser with { AvailableAllocation = currentUser.AvailableAllocation - amountToAllocate };
                    if (_users.TryUpdate(username, newUser, currentUser))
                    {
                        Console.WriteLine($"Allocated {amountToAllocate:P} to user '{username}'. Remaining: {newUser.AvailableAllocation:P}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to update allocation for user '{username}' due to concurrent modification.");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine($"Insufficient allocation for user '{username}'. Requested: {amountToAllocate:P}, Available: {currentUser.AvailableAllocation:P}");
                    return false;
                }
            }
            return false;
        }

        public void Deallocate(string username, decimal amountToDeallocate)
        {
            if (_users.TryGetValue(username, out var currentUser))
            {
                var newAllocation = Math.Min(1.0m, currentUser.AvailableAllocation + amountToDeallocate);
                var newUser = currentUser with { AvailableAllocation = newAllocation };
                if (_users.TryUpdate(username, newUser, currentUser))
                {
                    Console.WriteLine($"Deallocated {amountToDeallocate:P} from user '{username}'. New available: {newUser.AvailableAllocation:P}");
                }
                else
                {
                    Console.WriteLine($"Failed to deallocate for user '{username}' due to concurrent modification. Allocation might be inaccurate.");
                }
            }
        }
    }
}
