using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using ChatApp.Server.Models;
using ChatApp.Server.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;

namespace ChatApp.Server;

internal static class ChatAppExtensions
{
    // this should happen before AddChatServices...
    internal static void AddOptions(this IServiceCollection services, IConfiguration config)
    {
        // FrontendSettings class needs work on json serialization before this is useful...
        services.AddOptions<FrontendSettings>().Bind(config.GetSection(nameof(FrontendSettings)));

        services.AddOptions<KernelMemoryConfig>().BindConfiguration("KernelMemory");
        services.AddOptions<AzureAISearchConfig>().BindConfiguration("KernelMemory:Services:AzureAISearch");
        services.AddOptions<SearchClientConfig>().BindConfiguration("KernelMemory:Retrieval:SearchClient");

        // named options
        services.AddOptions<AzureOpenAIConfig>("AzureOpenAIText").BindConfiguration("KernelMemory:Services:AzureOpenAIText");
        services.AddOptions<AzureOpenAIConfig>("AzureOpenAIEmbedding").BindConfiguration("KernelMemory:Services:AzureOpenAIEmbedding");
    }

    internal static void AddChatAppServices(this IServiceCollection services, IConfiguration config)
    {
        var frontendSettings = config.GetSection(nameof(FrontendSettings)).Get<FrontendSettings>();

        var defaultAzureCreds = string.IsNullOrEmpty(config["AZURE_TENANT_ID"]) ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = config["AZURE_TENANT_ID"] });

        services.AddScoped<ChatCompletionService>();

        services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<AISearchOptions>>().Value ?? throw new Exception($"{nameof(AISearchOptions)} is required in settings.");

            if (string.IsNullOrWhiteSpace(options?.ApiKey))
            {
                var adOptions = services.GetRequiredService<IOptions<AzureAdOptions>>().Value;                

                return new SearchClient(
                        new Uri(options!.Endpoint),
                        options.IndexName,
                        defaultAzureCreds);
            }

            return new SearchClient(
                    new Uri(options.Endpoint),
                    options.IndexName,
                    new AzureKeyCredential(options.ApiKey));
        });

        services.AddSingleton<IKernelMemory>(services =>
        {            
            KernelMemoryConfig memoryConfiguration = services.GetRequiredService<IOptions<KernelMemoryConfig>>().Value ?? 
                throw new Exception("KernelMemory is required in settings.");
            AzureOpenAIConfig azureOpenAITextConfig = services.GetRequiredService<IOptionsMonitor<AzureOpenAIConfig>>().Get("AzureOpenAIText") ?? 
                throw new Exception("AzureOpenAIText is required in settings.");
            AzureOpenAIConfig azureOpenAIEmbeddingConfig = services.GetRequiredService<IOptionsMonitor<AzureOpenAIConfig>>().Get("AzureOpenAIEmbedding") ??
                throw new Exception("AzureOpenAIEmbedding is required in settings.");
            SearchClientConfig searchClientConfig = services.GetRequiredService<IOptions<SearchClientConfig>>().Value ??
                throw new Exception("SearchClientConfig is required in settings.");
            AzureAISearchConfig azureAISearchConfig = services.GetRequiredService<IOptions<AzureAISearchConfig>>().Value ??
                throw new Exception("AzureAISearchConfig is required in settings.");

            if (!string.IsNullOrEmpty(config["AZURE_TENANT_ID"]))
            {
                // only use set credential if overriding the tenantID from settings
                azureOpenAITextConfig.SetCredential(defaultAzureCreds);
                azureOpenAIEmbeddingConfig.SetCredential(defaultAzureCreds);
                azureAISearchConfig.SetCredential(defaultAzureCreds);
            }

            var kmBuilder = new KernelMemoryBuilder()
                .Configure(builder => builder.Services.AddLogging(l =>
                {
                    l.SetMinimumLevel(LogLevel.Trace);
                    l.AddSimpleConsole(c => c.SingleLine = true);
                }))
                .AddSingleton(memoryConfiguration)
                .WithAzureAISearchMemoryDb(azureAISearchConfig)              // Store memories in Azure AI Search
                .WithAzureOpenAITextGeneration(azureOpenAITextConfig)
                .WithAzureOpenAITextEmbeddingGeneration(azureOpenAIEmbeddingConfig);

            return kmBuilder.Build<MemoryServerless>();
        });

        var isChatEnabled = frontendSettings?.HistoryEnabled ?? false;

        if (isChatEnabled)
        {
            services.AddSingleton(services =>
            {
                var options = services.GetRequiredService<IOptions<CosmosOptions>>().Value ?? throw new Exception($"{nameof(CosmosOptions)} is rquired in settings.");

                return string.IsNullOrEmpty(options?.CosmosKey)
                    ? new CosmosClientBuilder(options!.CosmosEndpoint, defaultAzureCreds)
                        .WithSerializerOptions(new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase })
                        .Build()
                    : new CosmosClientBuilder(options.CosmosEndpoint, new AzureKeyCredential(options.CosmosKey))
                        .WithSerializerOptions(new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase })
                        .Build();
            });

            services.AddSingleton<CosmosConversationService>();
        }
    }
}
