using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Agents;
using WheelHouse.Infrastructure.Embeddings;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Prompts;
using WheelHouse.Infrastructure.Services;
using WheelHouse.Infrastructure.Vector;

namespace WheelHouse.Infrastructure;

/// <summary>Selects the RAG vector store backend.</summary>
public class VectorStoreOptions
{
    /// <summary>"auto" (prefer sqlite-vec when available), "sqlite-vec", or "cosine".</summary>
    public string Backend { get; set; } = "auto";
}

/// <summary>Registers WheelHouse infrastructure services (DB, agents, RAG).</summary>
public static class DependencyInjection
{
    /// <summary>Default on-disk SQLite location under LocalApplicationData.</summary>
    public static string DefaultDatabasePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WheelHouse");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "wheelhouse.db");
    }

    public static IServiceCollection AddWheelHouseInfrastructure(
        this IServiceCollection services, string? databasePath = null)
    {
        var dbPath = databasePath ?? DefaultDatabasePath();
        var connection = $"Data Source={dbPath}";

        services.AddDbContextFactory<WheelHouseDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<WheelHouseDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<WheelHouseDbContext>>().CreateDbContext());

        // Gemini (planning + cloud embeddings).
        services.AddSingleton(new GeminiOptions());
        services.AddHttpClient<IGeminiService, GeminiService>(c =>
            c.Timeout = TimeSpan.FromSeconds(120));

        services.AddSingleton<ICodeCompressionService, CodeCompressionService>();
        services.AddSingleton(new HeadroomOptions());
        services.AddSingleton<IAgentOrchestrator, ClaudeCliService>();
        services.AddScoped<WorkspaceConfigService>();
        services.AddScoped<IPromptTemplateService, PromptTemplateService>();
        services.AddScoped<ITranscriptSearch, TranscriptSearchService>();
        services.AddScoped<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IVerificationRunner, PowerShellVerificationRunner>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<IWorkspaceIndexQueue, WorkspaceIndexQueue>();
        services.AddHostedService<WorkspaceIndexingService>();

        // Template-driven flow: keyed implementations + resolver
        services.AddKeyedScoped<IPlanningService, GeminiPlanningService>("Gemini");
        services.AddKeyedSingleton<ITaskOrchestrationService, AgentOrchestratorService>("ClaudeCode");
        services.AddScoped<ISessionFlowResolver, SessionFlowResolver>();

        AddEmbeddings(services);
        AddVectorStore(services);
        services.AddScoped<IVectorSearchService, VectorSearchService>();

        return services;
    }

    /// <summary>Local-first embedding selection: on-device ONNX when present, else Gemini.</summary>
    private static void AddEmbeddings(IServiceCollection services)
    {
        services.AddSingleton(new EmbeddingOptions());
        services.AddSingleton<LocalOnnxEmbeddingProvider>();
        services.AddSingleton<GeminiEmbeddingProvider>();

        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var options = sp.GetRequiredService<EmbeddingOptions>();
            var local = sp.GetRequiredService<LocalOnnxEmbeddingProvider>();

            var preferLocal = options.Backend.ToLowerInvariant() switch
            {
                "local" => true,
                "gemini" => false,
                _ => local.IsAvailable // "auto"
            };

            return preferLocal && local.IsAvailable
                ? local
                : sp.GetRequiredService<GeminiEmbeddingProvider>();
        });
    }

    /// <summary>ANN-first store selection: sqlite-vec when the extension loads, else cosine.</summary>
    private static void AddVectorStore(IServiceCollection services)
    {
        services.AddSingleton(new VectorStoreOptions());
        services.AddSingleton<SqliteVecLoader>();
        services.AddScoped<CosineVectorStore>();
        services.AddScoped<SqliteVecVectorStore>();

        services.AddScoped<IVectorStore>(sp =>
        {
            var options = sp.GetRequiredService<VectorStoreOptions>();
            var loader = sp.GetRequiredService<SqliteVecLoader>();

            var preferVec = options.Backend.ToLowerInvariant() switch
            {
                "sqlite-vec" => true,
                "cosine" => false,
                _ => loader.Available // "auto"
            };

            return preferVec && loader.Available
                ? sp.GetRequiredService<SqliteVecVectorStore>()
                : sp.GetRequiredService<CosineVectorStore>();
        });
    }
}
