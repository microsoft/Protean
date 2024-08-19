using ChatApp.Server.Models;
using ChatApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;

namespace ChatApp.Server;

public static partial class Endpoints
{
    // can we simplify this to just use SK models? https://github.com/microsoft/semantic-kernel/discussions/5815

    public static WebApplication MapHistoryEndpoints(this WebApplication app)
    {
        app.MapGet("/history/ensure", GetEnsureHistoryAsync);
        app.MapPost("/history/clear", ClearHistoryAsync);
        app.MapDelete("/history/delete_all", DeleteAllHistoryAsync);
        app.MapPost("/history/rename", RenameHistoryAsync);
        app.MapDelete("/history/delete", DeleteHistory);
        app.MapPost("/history/message_feedback", MessageFeedbackAsync);
        app.MapGet("/history/list", ListHistoryAsync);
        app.MapPost("/history/read", ReadHistoryAsync);
        app.MapPost("/history/generate", GenerateHistoryAsync);

        return app;
    }

    private static async Task<IResult> GenerateHistoryAsync(
        HttpContext context,
        [FromBody] ConversationRequest conversation,
        [FromServices] CosmosConversationService history,
        [FromServices] ChatCompletionService chat)
    {
        var user = GetUser(context);
        string conversationId = conversation.Id;

        if (user == null)
            return Results.Unauthorized();

        if (conversation == null)
            return Results.BadRequest();

        // --- See if this is an existing conversation, otherwise create a new one ---
        var historyMetadata = new Dictionary<string, string>();

        Conversation? conversationHistory = await history.GetConversationAsync(user.UserPrincipalId, conversationId);
        if (conversationHistory == null)
        {
            var title = await chat.GenerateTitleAsync(conversation.Messages);

            // should we persist user message here too?
            conversationHistory = await history.CreateConversationAsync(user.UserPrincipalId, title);

            historyMetadata.Add("title", conversationHistory.Title);
            historyMetadata.Add("date", conversationHistory.CreatedAt.ToString());
        }
                
        historyMetadata.Add("conversation_id", conversationHistory.Id);

        // Format the incoming message object in the "chat/completions" messages format
        // then write it to the conversation history in cosmos
        var userMessage = conversation.Messages.LastOrDefault(m => m.Role.Equals(AuthorRole.User.ToString(), StringComparison.OrdinalIgnoreCase));
        if (userMessage == null)
            return Results.BadRequest("No user messages found");
        conversationHistory.Messages.Add(new HistoryMessage(userMessage));
        _ = await history.UpdateConversationAsync(user.UserPrincipalId, conversationHistory);

        // --- Do the chat completion ---
        // --- Write the completion messages to the conversation history in cosmos ---
        var completionResponse = await chat.CompleteChatAsync([.. conversation.Messages], true);
        var completionResult = new ChatCompletion
        {
            Id = Guid.NewGuid().ToString(),
            ApimRequestId = Guid.NewGuid().ToString(),
            Created = DateTime.UtcNow,
            Choices = [new() {
                Messages = [.. completionResponse]
            }],
            HistoryMetadata = historyMetadata
        };

        conversationHistory!.Messages = [.. completionResponse.Select(m => new HistoryMessage(m))];
        _ = await history.UpdateConversationAsync(user.UserPrincipalId, conversationHistory);

        return Results.Ok(completionResult);
    }

    private static async Task<IResult> GetEnsureHistoryAsync(HttpContext httpContext, [FromServices] CosmosConversationService history)
    {
        var (cosmosIsConfigured, _) = await history.EnsureAsync();

        return cosmosIsConfigured
            ? Results.Ok(JsonSerializer.Deserialize<object>(@"{ ""converation"": ""CosmosDB is configured and working""}"))
            : Results.NotFound(JsonSerializer.Deserialize<object>(@"{ ""error"": ""CosmosDB is not configured""}"));
    }

    private static async Task<IResult> ClearHistoryAsync(
        HttpContext context,
        Conversation conversation,
        [FromServices] CosmosConversationService history)
    {
        // get the user id from the request headers
        var user = GetUser(context);

        if (user == null)
            return Results.Unauthorized();


        if (string.IsNullOrWhiteSpace(conversation?.Id))
            return Results.BadRequest("conversation_id is required");
        
        var dbConversation = await history.GetConversationAsync(user.UserPrincipalId, conversation.Id) ?? throw new Exception($"Could not find conversation with id: {conversation.Id}");

        dbConversation.Messages.Clear();
        var resp = await history.UpdateConversationAsync(user.UserPrincipalId, dbConversation);

        return resp != null
            ? Results.Ok(new { message = "Successfully deleted messages in conversation", conversation_id = conversation.Id })
            : Results.NotFound();
    }

    private static async Task<IResult> DeleteAllHistoryAsync(HttpContext context, [FromServices] CosmosConversationService conversationService)
    {
        // get the user id from the request headers
        var user = GetUser(context);

        if (user == null)
            return Results.Unauthorized();

        await conversationService.DeleteConversationsAsync(user.UserPrincipalId);

        return Results.Ok(new
        {
            message = $"Successfully deleted conversation and messages for user {user.UserPrincipalId}"
        });
    }

    private static async Task<IResult> RenameHistoryAsync(
        HttpContext context,
        [FromBody] Conversation conversation,
        [FromServices] CosmosConversationService history)
    {
        var user = GetUser(context);

        if (user == null)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(conversation?.Id))
            return Results.BadRequest("conversation_id is required");

        var dbConversation = await history.GetConversationAsync(user.UserPrincipalId, conversation.Id) ?? throw new Exception($"Could not find conversation with id: {conversation.Id}");
        dbConversation.Title = conversation.Title;
        var updatedConversation = await history.UpdateConversationAsync(user.UserPrincipalId, dbConversation);

        if (updatedConversation == null)
            return Results.NotFound(new { error = $"Conversation {conversation.Id} was not found" });

        return Results.Ok(updatedConversation);
    }

    private static async Task<IResult> DeleteHistory(
        HttpContext context,
        [FromBody] Conversation conversation,
        [FromServices] CosmosConversationService conversationService)
    {
        var user = GetUser(context);

        if (user == null)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(conversation?.Id))
            return Results.BadRequest("conversation_id is required");

        _ = await conversationService.DeleteConversationAsync(user.UserPrincipalId, conversation.Id);

        var response = new
        {
            message = "Successfully deleted conversation and messages",
            conversation_id = conversation.Id
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> MessageFeedbackAsync(
        HttpContext context,
        [FromBody] HistoryMessage message,
        [FromServices] CosmosConversationService conversationService)
    {
        var user = GetUser(context);

        if (user == null)
            return Results.Unauthorized();

        var updatedMessage = await conversationService.UpdateMessageFeedbackAsync(user.UserPrincipalId, message.ConversationId, message.Id, message.Feedback);

        return updatedMessage != null
            ? Results.Ok(updatedMessage)
            : Results.NotFound();
    }

    private static async Task<IResult> ListHistoryAsync(
        HttpContext context,
        [FromServices] ChatCompletionService chat,
        [FromServices] CosmosConversationService history,
        int offset)
    {
        var user = GetUser(context);

        if (user == null)
            return Results.Unauthorized();

        var convosAsync = history.GetConversationsAsync(user.UserPrincipalId, 25, offset: offset);

        return Results.Ok(await convosAsync.ToListAsync());
    }

    private static async Task<IResult> ReadHistoryAsync(
        HttpContext context,
        [FromBody] Conversation conversation,
        [FromServices] CosmosConversationService history)
    {
        var user = GetUser(context);

        if (user == null)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(conversation?.Id))
            return Results.BadRequest("conversation_id is required");

        var dbConversation = await history.GetConversationAsync(user.UserPrincipalId, conversation.Id);

        if (dbConversation == null)
        {
            return Results.NotFound(new { ErrorEventArgs = $"Conversation {conversation.Id} was not found. It either does not exist or the logged in user does not have access to it." });
        }        

        return Results.Ok(new { conversation_id = dbConversation.Id, messages = dbConversation.Messages });
    }

    #region Helpers

    private static EasyAuthUser? GetUser(HttpContext context)
    {
        // return a default user if we're in development mode otherwise return null
        if (!context.Request.Headers.TryGetValue("X-Ms-Client-Principal-Id", out var principalId))
            return !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase)
                ? new() // todo: should we also use a test static json of Easy Auth headers to be injected into HttpContext in development mode?
                : null;

        return new EasyAuthUser
        {
            UserPrincipalId = principalId.FirstOrDefault() ?? string.Empty,
            Username = context.Request.Headers["X-Ms-Client-Principal-Name"].FirstOrDefault() ?? string.Empty,
            AuthProvider = context.Request.Headers["X-Ms-Client-Principal-Idp"].FirstOrDefault() ?? string.Empty,
            AuthToken = context.Request.Headers["X-Ms-Token-Aad-Id-Token"].FirstOrDefault() ?? string.Empty,
            ClientPrincipalB64 = context.Request.Headers["X-Ms-Client-Principal"].FirstOrDefault() ?? string.Empty,
            AadIdToken = context.Request.Headers["X-Ms-Token-Aad-Id-Token"].FirstOrDefault() ?? string.Empty
        };
    }

    #endregion
}
