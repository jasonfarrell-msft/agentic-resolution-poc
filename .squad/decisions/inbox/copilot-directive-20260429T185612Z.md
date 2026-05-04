### 2026-04-29T18:56:12Z: User directive
**By:** Jason Farrell (via Copilot)
**What:** Do NOT use standalone Azure OpenAI (`Microsoft.CognitiveServices/accounts` kind: `OpenAI`). Use Azure AI Foundry integrated AI Services (`kind: AIServices`) — the current non-deprecated approach. All model deployments go through AI Foundry.
**Why:** Standalone Azure OpenAI service is deprecated in favor of Microsoft Foundry / AI Services.
