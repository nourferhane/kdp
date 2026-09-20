using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.Configuration;

/// <summary>
/// Builds <see cref="FactorySettings"/> from runtime configuration. This is
/// the single place where non-secret factory settings reach agents.
/// </summary>
public sealed class FactorySettingsProvider(
    IOptions<AiProviderOptions> aiProvider,
    IOptions<OpenAiOptions> openAi,
    IOptions<GeminiOptions> gemini,
    IOptions<GoogleOptions> google) : IFactorySettingsProvider
{
    public Task<FactorySettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = new FactorySettings
        {
            Marketplace = "Amazon.com",
            Language = "English",
            MarketplaceConstraints =
            [
                "No content that violates Amazon KDP Content Guidelines.",
                "No trademarks, celebrity names, or licensed IP (including Pokemon, Minecraft, Roblox, etc.)",
                "No medical, legal, or financial advice.",
                "No low-content scraped/duplicate books.",
                "Target age ranges must be clearly defined between 0-18.",
                "Picture/activity books must be evergreen (not tied to a single holiday unless explicitly seasonal).",
            ],
            AiProvider = string.IsNullOrWhiteSpace(aiProvider.Value.Provider) ? "OpenAI" : aiProvider.Value.Provider,
            DefaultModel = aiProvider.Value.Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(gemini.Value.Model) ? "gemini-3.8-flash" : gemini.Value.Model)
                : (string.IsNullOrWhiteSpace(openAi.Value.Model) ? "gpt-4o-mini" : openAi.Value.Model),
            EnableAiOrchestrator = true,
            MaxAgentRetries = aiProvider.Value.Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, gemini.Value.MaxRetries)
                : Math.Max(1, openAi.Value.MaxRetries),
            GooglePagesPerCall = 100,
            GoogleEnabled = google.Value.IsConfigured,
        };

        return Task.FromResult(settings);
    }
}