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
        services.Configure<AzureAdOptions>(config.GetSection(nameof(AzureAdOptions)));
        services.Configure<AISearchOptions>(config.GetSection(nameof(AISearchOptions)));
        services.Configure<OpenAIOptions>(config.GetSection(nameof(OpenAIOptions)));
        services.Configure<CosmosOptions>(config.GetSection(nameof(CosmosOptions)));
        services.Configure<StorageOptions>(config.GetSection(nameof(StorageOptions)));

        // FrontendSettings class needs work on json serialization before this is useful...
        services.Configure<FrontendSettings>(config.GetSection(nameof(FrontendSettings)));
    }

    internal static void AddChatAppServices(this IServiceCollection services, IConfiguration config)
    {
        var azureAdOptions = config.GetSection(nameof(AzureAdOptions)).Get<AzureAdOptions>();
        var frontendSettings = config.GetSection(nameof(FrontendSettings)).Get<FrontendSettings>();

        var defaultAzureCreds = string.IsNullOrEmpty(config["AZURE_TENANT_ID"]) ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = config["AZURE_TENANT_ID"] });

        services.AddSingleton<ChatCompletionService>();

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
            var config = services.GetRequiredService<IConfiguration>();

            KernelMemoryConfig memoryConfiguration = new();
            AzureOpenAIConfig azureOpenAITextConfig = new();
            AzureOpenAIConfig azureOpenAIEmbeddingConfig = new();
            SearchClientConfig searchClientConfig = new();
            AzureAISearchConfig azureAISearchConfig = new();

            config.BindSection("KernelMemory", memoryConfiguration);
            config.BindSection("KernelMemory:Services:AzureOpenAIText", azureOpenAITextConfig);
            config.BindSection("KernelMemory:Services:AzureOpenAIEmbedding", azureOpenAIEmbeddingConfig);
            //config.BindSection("KernelMemory:Services:AzureAIDocIntel", azDocIntelConfig);
            config.BindSection("KernelMemory:Services:AzureAISearch", azureAISearchConfig);
            //config.BindSection("KernelMemory:Services:AzureBlobs", azureBlobConfig);
            config.BindSection("KernelMemory:Retrieval:SearchClient", searchClientConfig);

            if (!string.IsNullOrEmpty(config["AZURE_TENANT_ID"]))
            {
                var defaultAzureCreds = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = config["AZURE_TENANT_ID"] });
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
