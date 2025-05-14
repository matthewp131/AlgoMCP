using AlgoMCPServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace AlgoMCPServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly UserService _userService;

        public UserController(UserService userService)
        {
            _userService = userService;
        }

        public class AddUserRequest
        {
            public required string Username { get; set; }
            public required string ApiKey { get; set; }
            public required string ApiSecret { get; set; }
        }

        [HttpPost]
        public IActionResult AddUser([FromBody] AddUserRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body cannot be null.");
            }
            _userService.AddUser(request.Username, request.ApiKey, request.ApiSecret);
            return Ok($"User '{request.Username}' added.");
        }

        [HttpGet("{username}")]
        public IActionResult GetUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return BadRequest("Username cannot be empty.");
            }

            var user = _userService.GetUser(username);
            if (user == null)
            {
                return NotFound($"User '{username}' not found.");
            }

            // Create a new user object with empty ApiSecret
            var safeUser = user with { ApiSecret = string.Empty };
            return Ok(safeUser);
        }
    }
}
