import os
from dotenv import load_dotenv
from agent_framework import MCPStreamableHTTPTool

load_dotenv()

_mcp_tool: MCPStreamableHTTPTool | None = None


def create_mcp_tool() -> MCPStreamableHTTPTool:
    global _mcp_tool
    if _mcp_tool is None:
        mcp_url = os.environ.get(
            "MCP_SERVER_URL",
            "https://ca-mcp-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io/mcp"
        )
        _mcp_tool = MCPStreamableHTTPTool(
            name="tickets_api",
            url=mcp_url,
            approval_mode="never_require",
            load_prompts=False,
        )
    return _mcp_tool
