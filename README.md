# AlgoMCP: Algorithmic Trading Strategy Manager and Tools

This repository contains two main projects:

1.  **AlgoMCPServer**: An ASP.NET Core web server that manages and executes algorithmic trading strategies using the Alpaca trading API.
2.  **AlgoMCPTools**: A .NET Core MCP (Model Context Protocol) server, providing tools to interact with the `AlgoMCPServer` via its REST API.

## Quickstart

1. Run the HTTP Server
```bash
cd AlgoMCPServer
dotnet run
```

2. Call the APIs...
```bash
curl -X POST 'http://localhost:5238/api/user' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "username": "YOUR NAME HERE",
  "apiKey": "YOUR KEY HERE",
  "apiSecret": "YOUR SECRET HERE"
}'
curl -X POST 'http://localhost:5238/api/strategy/mean_reversion' \
  --header 'Content-Type: application/json' \
  --data-raw '{
  "username": "YOUR NAME HERE",
  "strategyName": "MeanReversionWithCrypto",
  "symbol": "DOGE/USD",
  "allocationPercentage": 0.5
}'
```

3. ...or ask AI to do it
  * Set up your IDE like in .vscode/mcp.json
  * Prompt your favorite LLM something like "create a user named matthew with api info ... on the algoMCPServer and start a trading strategy"

4. Don't forget to stop the trading with either
```bash
curl -X DELETE 'http://localhost:5238/api/strategy/{:username}'
```
or prompt the LLM "stop the trading strategy"

## Projects

### 1. AlgoMCPServer

*   **Role**: HTTP Server for managing and running trading strategies.
*   **Functionality**:
    *   Exposes REST API endpoints to:
        *   Manage users (add, get).
        *   Manage trading strategies (get available, initialize, stop).
    *   Connects to Alpaca for live trading and market data.
    *   Currently implements a "MeanReversionWithCrypto" strategy.
    *   Handles graceful shutdown of strategies.

#### Starting AlgoMCPServer

1.  **Prerequisites**:
    *   .NET SDK (version compatible with the project, e.g., .NET 8 or newer).
    *   Alpaca Paper Trading API Key and Secret.
2.  **Running the Server**:
    *   Navigate to the server project directory:
        ```bash
        cd d:\git\AlgoMCP\AlgoMCPServer
        ```
    *   Run the server:
        ```bash
        dotnet run
        ```
    *   By default, the server will start on `http://localhost:5238` (and possibly `https://localhost:7107` if HTTPS is configured).

#### OpenAPI Specification

The `AlgoMCPServer` exposes an OpenAPI (Swagger) specification for its API at GET /openapi/v1.json. This file is also saved in this repo at [AlgoMCPServer/openapi/v1.json](./AlgoMCPServer/openapi/v1.json)

### 2. AlgoMCPTools

*   **Role**: MCP Server providing command-line tools to interact with `AlgoMCPServer`.
*   **Functionality**:
    *   Exposes MCP tools that correspond to the REST API endpoints of `AlgoMCPServer`.
    *   Uses an HTTP client to communicate with `AlgoMCPServer`.
    *   Allows for programmatic or command-line interaction with the trading strategy manager.

#### Starting AlgoMCPTools

1.  **Prerequisites**:
    *   .NET SDK (version compatible with the project).
    *   `AlgoMCPServer` must be running and accessible (by default at `http://localhost:5238`).
2.  **Running the MCP Server**:
    *   .vscode/mcp.json shows how Copilot in vscode can interact with the MCP Tools

## Workflow

1.  Start `AlgoMCPServer` to make the trading strategy management and execution capabilities available via its REST API.
2.  Start `AlgoMCPTools` to provide an MCP interface to those capabilities.
3.  Use an MCP client (e.g., within VS Code if configured) to connect to `AlgoMCPTools` and execute commands like:
    *   Adding a user.
    *   Listing available strategies.
    *   Initializing a strategy for a user with a specific symbol and allocation.
    *   Stopping a user's strategy.

## Implemented Strategies

*   **MeanReversionWithCrypto**: A strategy that attempts to profit from an asset's price reverting to its historical mean. It is configured for cryptocurrency markets and uses Alpaca APIs for order execution and market data.

## Future Enhancements

*   Add more trading strategies.
*   Implement more robust error handling and logging.
*   Add authentication/authorization to `AlgoMCPServer` API endpoints.
*   Expand MCP tools for more detailed interaction (e.g., getting strategy status, P&L).
*   Database integration for persistent user and strategy data.
