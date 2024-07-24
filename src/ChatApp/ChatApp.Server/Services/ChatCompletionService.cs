#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
using Azure;
using Azure.Identity;
using ChatApp.Server.Models;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;

namespace ChatApp.Server.Services;

public class ChatCompletionService
{
    private readonly Kernel _kernel;
    private readonly OpenAIPromptExecutionSettings _promptSettings;
    private readonly string _promptDirectory;

    public ChatCompletionService(IOptions<OpenAIOptions> options, IOptions<AzureAdOptions> adOptions, AzureSearchService searchService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options?.Value?.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(options?.Value?.ChatDeployment);
        ArgumentException.ThrowIfNullOrWhiteSpace(options?.Value?.EmbeddingDeployment);

        _promptSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 1024,
            Temperature = 0.5,
            StopSequences = [],
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var builder = Kernel.CreateBuilder();

        if (string.IsNullOrEmpty(options.Value.ApiKey)) // use managed identity
        {
            var defaultAzureCreds = string.IsNullOrWhiteSpace(adOptions?.Value?.TenantId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(
                    new DefaultAzureCredentialOptions
                    {
                        TenantId = adOptions.Value.TenantId
                    });

            builder = builder.AddAzureOpenAITextEmbeddingGeneration(
            options.Value.EmbeddingDeployment,
            options.Value.Endpoint,
            defaultAzureCreds);

            builder = builder.AddAzureOpenAIChatCompletion(
            options.Value.ChatDeployment,
            options.Value.Endpoint,
            defaultAzureCreds);
        }
        else // use api key
        {
            builder = builder.AddAzureOpenAIChatCompletion(
                options.Value.EmbeddingDeployment,
                options.Value.Endpoint,
                options.Value.ApiKey);

            builder = builder.AddAzureOpenAIChatCompletion(
                options.Value.ChatDeployment,
                options.Value.Endpoint,
                options.Value.ApiKey);
        }

        _promptDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");

        //builder.Plugins.AddFromPromptDirectory(_promptDirectory);

        _kernel = builder.Build();
    }

    public async Task<ChatCompletion> CompleteChat(string prompt)
    {
        var msg = new Message
        {
            Id = "0000",
            Role = AuthorRole.User.ToString(),
            Content = prompt,
            Date = DateTime.UtcNow
        };

        return await CompleteChat([msg]);
    }

    public async Task<ChatCompletion> CompleteChat(Message[] messages)
    {
        //var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        //string documentContents = string.Empty;
        //if (messages.Any(m => m.Role.Equals(AuthorRole.Tool.ToString(), StringComparison.OrdinalIgnoreCase)))
        //{
        //    // parse out the document contents
        //    var toolContent = JsonSerializer.Deserialize<ToolContentResponse>(
        //        messages.First(m => m.Role.Equals(AuthorRole.Tool.ToString(), StringComparison.OrdinalIgnoreCase)).Content, options);
        //    documentContents = string.Join("\r", toolContent.Citations.Select(c => $"{c.Title}:{c.Content}:{c.AdditionalContent}"));
        //}
        //else
        //{
        //    documentContents = "no source available.";
        //}

        var sysmessage = $$$"""
                You are an a helpful agent answering questions based on information available to you, general information and functions.
                """;
        var history = new ChatHistory(sysmessage);

        //filter out 'tool' messages and add rest to history
        messages.Where(m => !m.Role.Equals(AuthorRole.Tool.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList()
            .ForEach(m => history.AddUserMessage(m.Content));

        var response = await _kernel.GetRequiredService<IChatCompletionService>().GetChatMessageContentAsync(history, _promptSettings, _kernel);
        // add assistant response message to history and return chatcompletion

        // append response messages to messages array
        var responseMessages = messages.ToList();

        response.Items.ToList().ForEach(item => responseMessages.Add(new Message
        {
            Id = Guid.NewGuid().ToString(),
            Role = AuthorRole.Assistant.ToString().ToLower(),
            Content = item.ToString()!,
            Date = DateTime.UtcNow
        }));

        var result = new ChatCompletion
        {
            Id = Guid.NewGuid().ToString(),
            ApimRequestId = Guid.NewGuid().ToString(),
            Model = response.ModelId!,
            Created = DateTime.UtcNow,
            Choices = [new() {
                Messages = [.. responseMessages]
            }]
        };

        return result;
    }

    public async Task<string> GenerateTitleAsync(List<Message> messages)
    {
        // Create a conversation string from the messages
        string conversationText = string.Join(" ", messages.Select(m => m.Role + " " + m.Content));

        // Load prompt yaml
        var promptYaml = File.ReadAllText(Path.Combine(_promptDirectory, "TextPlugin", "SummarizeConversation.yaml"));
        var function = _kernel.CreateFunctionFromPromptYaml(promptYaml);

        // Invoke the function against the conversation text
        var result = await _kernel.InvokeAsync(function, new() { { "history", conversationText } });

        string completion = result.ToString()!;

        return completion;
    }
}

#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
