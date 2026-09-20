using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Gemini;
using Zunavio.KdpFactory.Infrastructure.OpenAi;

namespace Zunavio.KdpFactory.Infrastructure.Ai;

public sealed class ProviderLanguageModelClient : ILanguageModelClient
{
    private readonly AiProviderOptions _provider;
    private readonly OpenAiLanguageModelClient _openAi;
    private readonly GeminiLanguageModelClient _gemini;

    public ProviderLanguageModelClient(
        IOptions<AiProviderOptions> provider,
        OpenAiLanguageModelClient openAi,
        GeminiLanguageModelClient gemini)
    {
        _provider = provider.Value;
        _openAi = openAi;
        _gemini = gemini;
    }

    public Task<LanguageModelResult> CompleteStructuredAsync(
        LanguageModelRequest request,
        CancellationToken ct) =>
        Normalize(_provider.Provider) switch
        {
            "gemini" => _gemini.CompleteStructuredAsync(request, ct),
            "openai" => _openAi.CompleteStructuredAsync(request, ct),
            var unsupported => throw new InvalidOperationException(
                $"Unsupported AI_PROVIDER '{unsupported}'. Supported values: OpenAI, Gemini.")
        };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "openai"
            : value.Trim().ToLowerInvariant();
}
