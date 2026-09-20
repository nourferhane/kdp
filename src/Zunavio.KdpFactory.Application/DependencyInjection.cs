using Microsoft.Extensions.DependencyInjection;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;
using Zunavio.KdpFactory.Application.HumanReview;
using Zunavio.KdpFactory.Application.Orchestration;
using Zunavio.KdpFactory.Application.Services;

namespace Zunavio.KdpFactory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddKdpApplication(this IServiceCollection services)
    {
        // ---- Core deterministic services ----
        services.AddSingleton<IHashService, Sha256HashService>();
        services.AddSingleton<IVersionService, VersionService>();

        // ---- State machinery (decision + validation, all in-process) ----
        services.AddSingleton<IWorkflowTransitionValidator, WorkflowTransitionValidator>();
        services.AddSingleton<IMandatoryReviewPolicy, MandatoryReviewPolicy>();
        services.AddSingleton<IAgentOutputValidator, AgentOutputValidator>();
        services.AddSingleton<DeterministicOrchestratorDecider>();

        // Level-2 decider wraps Level-1: its own AI is optional.
        services.AddScoped<AiOrchestratorDecider>();
        services.AddScoped<IOrchestratorDecider>(sp => sp.GetRequiredService<AiOrchestratorDecider>());

        // ---- Context builder ----
        services.AddScoped<IAgentContextBuilder, AgentContextBuilder>();

        // ---- Runtime configuration distilled for agents ----
        services.AddScoped(sp => sp.GetRequiredService<IFactorySettingsProvider>().GetAsync().Result);

        // ---- Services ----
        services.AddScoped<IBackgroundJobManager, BackgroundJobManager>();
        services.AddScoped<IOrchestrationEngine, OrchestrationEngine>();
        services.AddScoped<IOrchestratorService, OrchestratorService>();
        services.AddScoped<IHumanReviewService, HumanReviewService>();
        services.AddScoped<IProjectQueryService, ProjectQueryService>();
        services.AddScoped<IAgentDefinitionQueryService, AgentDefinitionQueryService>();
        services.AddScoped<IAgentPromptRefreshService, AgentPromptRefreshService>();
        services.AddScoped<IReviewQueryService, ReviewQueryService>();
        services.AddScoped<IJobQueryService, JobQueryService>();

        // ---- Repository façade (concrete repos live behind the unit of work) ----
        services.AddScoped<IProjectRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Projects);
        services.AddScoped<IAgentDefinitionRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Agents);
        services.AddScoped<IAgentRunRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Runs);
        services.AddScoped<IAssetRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Assets);
        services.AddScoped<IHumanReviewRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Reviews);
        services.AddScoped<IWorkflowEventRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Events);
        services.AddScoped<IBackgroundJobRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Jobs);

        // ---- Code generator (needs a database-backed sequence reader) ----
        services.AddSingleton<ICodeGenerator>(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            return new CodeGenerator(ct =>
            {
                using var scope = scopeFactory.CreateScope();
                return scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Projects.GetProjectSequenceAsync(ct);
            });
        });

        return services;
    }
}