using AlgoMCPServer.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AlgoMCPServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StrategyController : ControllerBase
    {
        private readonly StrategyService _strategyService;
        private readonly UserService _userService;

        // Enum for available strategies
        public enum AvailableStrategy
        {
            MeanReversionWithCrypto // Assuming this is the one currently implemented
            // Add other strategy names here as they become available
        }


        public StrategyController(StrategyService strategyService, UserService userService)
        {
            _strategyService = strategyService;
            _userService = userService;
        }

        [HttpGet]
        public IActionResult GetAvailableStrategies()
        {
            var strategyNames = Enum.GetNames(typeof(AvailableStrategy)).ToList();
            return Ok(strategyNames);
        }

        public class MeanReversionRequest
        {
            public required string Username { get; set; }
            public required string StrategyName { get; set; } // For future use to select different strategies
            public required string Symbol { get; set; }
            public decimal AllocationPercentage { get; set; }
        }

        [HttpPost("mean_reversion")]
        public IActionResult InitializeMeanReversionStrategy([FromBody] MeanReversionRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body cannot be null.");
            }

            // Basic validation for allocation percentage
            if (request.AllocationPercentage <= 0 || request.AllocationPercentage > 1)
            {
                return BadRequest("Account allocation percentage must be between 0 (exclusive) and 1 (inclusive).");
            }

            // For now, StrategyName from request is noted but InitializeStrategyAsync uses a hardcoded strategy.
            // This can be expanded later to dynamically instantiate strategies based on StrategyName.
            _strategyService.InitializeStrategyAsync(request.Username, request.Symbol, request.AllocationPercentage);
            return Ok($"Strategy initialization requested for user '{request.Username}' with symbol '{request.Symbol}'.");
        }

        [HttpDelete("{username}")]
        public async Task<IActionResult> StopUserStrategy(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return BadRequest("Username cannot be empty.");
            }
            await _strategyService.StopStrategy(username);
            return Ok($"Attempted to stop strategies for user '{username}'.");
        }
    }
}
