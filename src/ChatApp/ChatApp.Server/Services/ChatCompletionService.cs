#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
using ChatApp.Server.Models;
using ChatApp.Server.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ChatApp.Server.Services;

public class ChatCompletionService
{
    private readonly Kernel _kernel;
    private readonly OpenAIPromptExecutionSettings _promptSettings;
    private readonly string _promptDirectory;

    private const string MemberAssistantInstructions = $$$"""
        You are a helpful agent answering questions and automating actions on behalf of a user with the plugin functions available to you.
        Do not use general information. 
        Steps:
        1. Validate the card number and system (if it has not already been validated during the converstation. Ask for the card number if not provided. Respond back to the user with validation outcome and always include member id from response. 
        2. The validated system will inform you which tools you should use for the rest of the conversation. Do not use tools for other systems.
        3. Share any information retrieved with the user, include references to the source as a clickable document name with hyperlink to the document location, section name, and page number if available.
        4. If you cannot find information in the tools available to you, say "I cannot find information about that". Do not use general knowledge.
        5. If you have tools (within the validated system only) to perform tasks being discussed, offer to perform those tasks for the user. Do not offer to perform tasks you don't have tools to perform for the validated system. You must get explicit approval from the user before automating any task.
        """;

    public ChatCompletionService(Kernel kernel) 
    {
        _kernel = kernel;
        _promptSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 1024,
            Temperature = 0.5,
            StopSequences = [],
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        _promptDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");
    }    

    public async Task<Message[]> CompleteChatAsync(Message[] messages, bool agentEnabled = false)
    {
        if (agentEnabled)
            // Add the agent plugin to the kernel. All plugins specific to individual agents are registered with separate agent kernels inside this plugin.
        {
            _kernel.Plugins.AddFromType<AutomationAgentsPlugin>(serviceProvider: _kernel.Services);
        }
        else
            // With the agentic approach disabled, all plugins are registered with the primary kernel and used with automatic function calling. 
        {
            _kernel.Plugins.AddFromType<ReplaceCardPlugin>(serviceProvider: _kernel.Services);
            _kernel.Plugins.AddFromType<SystemOneSearchPlugin>(serviceProvider: _kernel.Services);
        }

        var history = new ChatHistory(MemberAssistantInstructions);

        messages = messages.Where(m => !string.IsNullOrWhiteSpace(m.Id)).ToArray();
        //filter out 'tool' messages and 'empty' messages, add rest to history
        messages.Where(m => !m.Role.Equals(AuthorRole.Tool.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList()
            .ForEach(m => history.AddMessage(ParseRole(m.Role), m.Content));

        var response = await _kernel.GetRequiredService<IChatCompletionService>().GetChatMessageContentAsync(history, _promptSettings, _kernel);

        // append response messages to messages array
        var responseMessages = messages.ToList();

        // tool calls and responses are added to the response messages
        history.Where(m => m.Role == AuthorRole.Tool).ToList().ForEach(item => responseMessages.Add(new Message
        {
            Id = Guid.NewGuid().ToString(),
            Role = AuthorRole.Tool.ToString().ToLower(),
            Content = item.ToString()!,
            Date = DateTime.UtcNow
        }));

        response.Items.ToList().ForEach(item => responseMessages.Add(new Message
        {
            Id = Guid.NewGuid().ToString(),
            Role = AuthorRole.Assistant.ToString().ToLower(),
            Content = item.ToString()!,
            Date = DateTime.UtcNow
        }));

        return [.. responseMessages];
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

    internal static AuthorRole ParseRole(string roleName)
    {
        switch (roleName.ToLower() ?? string.Empty)
        {
            case "user":
                return AuthorRole.User;
            case "assistant":
                return AuthorRole.Assistant;
            case "tool":
                return AuthorRole.Tool;
            case "system":
                return AuthorRole.System;
            default:
                return AuthorRole.User;
        }
    }    
}

#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
