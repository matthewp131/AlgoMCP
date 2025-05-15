using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlgoMCPTools
{
    [McpServerToolType]
    public static class StrategyTools
    {
        [McpServerTool, Description("Get all available trading strategies")]
        public static async Task<string> GetStrategies(HttpClient httpClient)
        {
            try
            {
                var response = await httpClient.GetAsync("/api/strategy");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return content;
            }
            catch (Exception ex)
            {
                return $"Error getting strategies: {ex.Message}";
            }
        }

        [McpServerTool, Description("Initialize a mean reversion trading strategy for a user")]
        public static async Task<string> InitializeMeanReversionStrategy(HttpClient httpClient, string username, string strategyName, string symbol, decimal allocationPercentage)
        {
            try
            {
                var request = new
                {
                    Username = username,
                    StrategyName = strategyName,
                    Symbol = symbol,
                    AllocationPercentage = allocationPercentage
                };

                var response = await httpClient.PostAsJsonAsync("/api/strategy/mean_reversion", request);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return content;
            }
            catch (Exception ex)
            {
                return $"Error initializing strategy: {ex.Message}";
            }
        }

        [McpServerTool, Description("Stop all trading strategies for a user")]
        public static async Task<string> StopUserStrategy(HttpClient httpClient, string username)
        {
            try
            {
                var response = await httpClient.DeleteAsync($"/api/strategy/{username}");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return content;
            }
            catch (Exception ex)
            {
                return $"Error stopping strategy: {ex.Message}";
            }
        }
    }
}