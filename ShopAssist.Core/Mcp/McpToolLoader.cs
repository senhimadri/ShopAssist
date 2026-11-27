using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace ShopAssist.Core.Mcp;

public static class McpToolLoader
{
    /// <summary>
    /// Starts the orders MCP server as a child process, connects over stdio,
    /// and returns its tools as AIFunctions the agent can use.
    /// </summary>
    public static async Task<(McpClient Client, IList<AIFunction> Tools)> ConnectAsync(string serverProjectPath)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "orders",
            Command = "dotnet",
            Arguments = ["run", "--no-build", "--project", serverProjectPath]
        });

        var client = await McpClient.CreateAsync(transport);

        // Discovery: ask the server what it can do. No hard-coded tool list in ShopAssist.
        var tools = await client.ListToolsAsync();
        return (client, tools.Cast<AIFunction>().ToList());
    }
}