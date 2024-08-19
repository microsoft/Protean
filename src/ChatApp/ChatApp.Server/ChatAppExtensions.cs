using Amazon.Runtime;
using Azure;
using Azure.Identity;
using ChatApp.Server.Models;
using ChatApp.Server.Plugins;
using ChatApp.Server.Services;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace ChatApp.Server;

internal static class ChatAppExtensions
{
    // this should happen before AddChatServices...
    internal static void AddOptions(this IServiceCollection services, IConfiguration config)
    {
        // FrontendSettings class needs work on json serialization before this is useful...
        services.AddOptions<FrontendSettings>().Bind(config.GetSection(nameof(FrontendSettings)));
        services.AddOptions<CosmosOptions>().Bind(config.GetSection(nameof(CosmosOptions)));

        services.AddOptions<KernelMemoryConfig>().BindConfiguration("KernelMemory");
        services.AddOptions<AzureAISearchConfig>().BindConfiguration("KernelMemory:Services:AzureAISearch");
        services.AddOptions<SearchClientConfig>().BindConfiguration("KernelMemory:Retrieval:SearchClient");
        services.AddOptions<AzureBlobsConfig>().BindConfiguration("KernelMemory:Services:AzureBlobs");

        // named options
        services.AddOptions<AzureOpenAIConfig>("AzureOpenAIText").BindConfiguration("KernelMemory:Services:AzureOpenAIText");
        services.AddOptions<AzureOpenAIConfig>("AzureOpenAIEmbedding").BindConfiguration("KernelMemory:Services:AzureOpenAIEmbedding");

        services.AddOptions<KnowledgeBaseOptions>().Bind(config.GetSection(nameof(KnowledgeBaseOptions)));
        services.AddOptions<AuthOptions>().Bind(config.GetSection(nameof(AuthOptions)));
    }

    internal static void AddChatAppServices(this IServiceCollection services, IConfiguration config)
    {
        var frontendSettings = config.GetSection(nameof(FrontendSettings)).Get<FrontendSettings>();

        var defaultAzureCreds = string.IsNullOrEmpty(config["AZURE_TENANT_ID"]) ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = config["AZURE_TENANT_ID"] });

        services.AddScoped<ChatCompletionService>();

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
            AzureBlobsConfig azureBlobsConfig = services.GetRequiredService<IOptions<AzureBlobsConfig>>().Value ??
                throw new Exception("AzureBlobsConfig is required in settings.");
           

            if (!string.IsNullOrEmpty(config["AZURE_TENANT_ID"]))
            {
                // only use set credential if overriding the tenantID from settings
                azureOpenAITextConfig.SetCredential(defaultAzureCreds);
                azureOpenAIEmbeddingConfig.SetCredential(defaultAzureCreds);
                azureAISearchConfig.SetCredential(defaultAzureCreds);
                azureBlobsConfig.SetCredential(defaultAzureCreds);
                 
            }

            var kmBuilder = new KernelMemoryBuilder()
                .Configure(builder => builder.Services.AddLogging(l =>
                {
                    l.SetMinimumLevel(LogLevel.Trace);
                    l.AddSimpleConsole(c => c.SingleLine = true);
                }))
                .AddSingleton(memoryConfiguration)
                .WithAzureAISearchMemoryDb(azureAISearchConfig)              // Store memories in Azure AI Search
                .WithAzureBlobsDocumentStorage(azureBlobsConfig)              // Store files in Azure Blobs
                .WithAzureOpenAITextGeneration(azureOpenAITextConfig)
                .WithAzureOpenAITextEmbeddingGeneration(azureOpenAIEmbeddingConfig);

            return kmBuilder.Build<MemoryServerless>();
        });

        services.AddScoped(services =>
        {
            // Get our dependencies
            var kernelMemory = services.GetRequiredService<IKernelMemory>();
            var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
            var jsonOptions = services.GetRequiredService<JsonSerializerOptions>();
            AzureOpenAIConfig azureOpenAITextConfig = services.GetRequiredService<IOptionsMonitor<AzureOpenAIConfig>>().Get("AzureOpenAIText") ??
                throw new Exception("AzureOpenAIText is required in settings.");
            KnowledgeBaseOptions knowledgebaseOptions = services.GetRequiredService<IOptions<KnowledgeBaseOptions>>().Value ??
                throw new Exception("KnowledgeBaseOptions is required in settings.");
            AuthOptions authOptions = services.GetRequiredService<IOptions<AuthOptions>>().Value ??
                throw new Exception("AuthOptions is required in settings.");

            // Create the KernelBuilder
            var builder = Kernel.CreateBuilder();

            // register dependencies with Kernel services collection
            builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));
            builder.Services.AddSingleton(azureOpenAITextConfig);
            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton(kernelMemory);
            builder.Services.AddSingleton(jsonOptions);
            builder.Services.AddSingleton(httpClientFactory);
            builder.Services.AddSingleton(knowledgebaseOptions);
            builder.Services.AddSingleton(authOptions);

            if (string.IsNullOrEmpty(azureOpenAITextConfig.APIKey)) // use managed identity
            { // use non-home tenant if specified
                var defaultAzureCreds = string.IsNullOrWhiteSpace(config["AZURE_TENANT_ID"])
                    ? new DefaultAzureCredential()
                    : new DefaultAzureCredential(
                        new DefaultAzureCredentialOptions
                        {
                            TenantId = config["AZURE_TENANT_ID"]
                        });

                builder = builder.AddAzureOpenAIChatCompletion(
                    azureOpenAITextConfig.Deployment,
                    azureOpenAITextConfig.Endpoint,
                    defaultAzureCreds);
            }
            else // use api key
            {
                builder = builder.AddAzureOpenAIChatCompletion(
                    azureOpenAITextConfig.Deployment,
                    azureOpenAITextConfig.Endpoint,
                    azureOpenAITextConfig.APIKey);
            }

            // Register the native plugins with the primary kernel
            builder.Plugins.AddFromType<ValidatePersonPlugin>();

            return builder.Build();
        });

        services.AddSingleton(services => new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var isChatEnabled = frontendSettings?.HistoryEnabled ?? false;

        if (isChatEnabled)
        {
            services.AddSingleton(services =>
            {
                var options = services.GetRequiredService<IOptions<CosmosOptions>>().Value ?? throw new Exception($"{nameof(CosmosOptions)} is rquired in settings.");

                return string.IsNullOrEmpty(options?.CosmosKey)
                    ? new CosmosClientBuilder(options!.CosmosEndpoint, defaultAzureCreds)
                        .WithSerializerOptions(new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase })
                        .WithConnectionModeGateway()
                        .Build()
                    : new CosmosClientBuilder(options.CosmosEndpoint, new AzureKeyCredential(options.CosmosKey))
                        .WithSerializerOptions(new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase })
                        .WithConnectionModeGateway()
                        .Build();
            });

            services.AddSingleton<CosmosConversationService>();
        }
    }

}
