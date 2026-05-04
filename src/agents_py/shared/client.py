import os
from dotenv import load_dotenv
from azure.identity import DefaultAzureCredential
from agent_framework_openai import OpenAIChatCompletionClient

load_dotenv()

_client: OpenAIChatCompletionClient | None = None


def get_client() -> OpenAIChatCompletionClient:
    global _client
    if _client is None:
        endpoint = os.environ.get(
            "AZURE_OPENAI_ENDPOINT",
            "https://oai-agentic-res-src-dev.cognitiveservices.azure.com/"
        )
        model = os.environ.get("AZURE_OPENAI_MODEL", "gpt-5.1-deployment")
        api_version = os.environ.get("AZURE_OPENAI_API_VERSION", "2024-12-01-preview")
        _client = OpenAIChatCompletionClient(
            model=model,
            azure_endpoint=endpoint,
            api_version=api_version,
            credential=DefaultAzureCredential(),
        )
    return _client
