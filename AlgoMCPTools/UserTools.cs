using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace AlgoMCPTools
{
    [McpServerToolType]
    public static class UserTools
    {
        [McpServerTool, Description("Add a new user")]
        public static async Task<string> AddUser(HttpClient httpClient, string username, string apiKey, string apiSecret)
        {
            try
            {
                var request = new
                {
                    Username = username,
                    ApiKey = apiKey,
                    ApiSecret = apiSecret
                };

                var response = await httpClient.PostAsJsonAsync("/api/user", request);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return content;
            }
            catch (Exception ex)
            {
                return $"Error adding user: {ex.Message}";
            }
        }

        [McpServerTool, Description("Get user details")]
        public static async Task<string> GetUser(HttpClient httpClient, string username)
        {
            try
            {
                var response = await httpClient.GetAsync($"/api/user/{username}");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return content;
            }
            catch (Exception ex)
            {
                return $"Error getting user: {ex.Message}";
            }
        }
    }
}