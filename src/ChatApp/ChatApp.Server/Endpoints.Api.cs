
using ChatApp.Server.Models;
using ChatApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;

namespace ChatApp.Server;

public static partial class Endpoints
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapGet("/frontend_settings", ([FromServices] IOptions<FrontendSettings> settings) => settings.Value)
            .WithName("GetFrontendSettings")
            .WithOpenApi();

        app.MapPost("/agentchat", ([FromServices] ChatCompletionService chat, [FromBody] ConversationRequest history) => chat.CompleteChatAsync([.. history.Messages], true));
        app.MapPost("/conversation", async ([FromServices] ChatCompletionService chat, [FromBody] ConversationRequest history) =>
            new ChatCompletion
            {
                Id = Guid.NewGuid().ToString(),
                ApimRequestId = Guid.NewGuid().ToString(),
                Created = DateTime.UtcNow,
                Choices = [new() {
                Messages = [.. await chat.CompleteChatAsync([.. history.Messages], true)]
            }]
            });
        app.MapPost("/search", ([FromServices] IKernelMemory search, [FromQuery] string query) => search.SearchAsync(query));
        app.MapPost("/ask", ([FromServices] IKernelMemory search, [FromQuery] string query) => search.AskAsync(query));
        app.MapGet("/download", GetDownloadDocumentAsync);

        return app;
    }

    /// <summary>
    /// Use the Kernel Memory service to download document from Azure Blob Storage.
    /// Ensure document exists in configured storage container.
    /// container/index/documentId/filename
    /// (docs/default/doc001/Document1.pdf)
    /// </summary>
    /// <param name="search">The IKernelMemory service</param>
    /// <param name="index"></param>
    /// <param name="documentId"></param>
    /// <param name="filename"></param>
    /// <returns>File stream</returns>
    private static async Task<IResult> GetDownloadDocumentAsync(
        [FromServices] IKernelMemory search,
        [FromQuery] string index,
        [FromQuery] string documentId,
        [FromQuery] string filename)
    {
        var document = await search.ExportFileAsync(documentId, filename, index);
        var stream = await document.GetStreamAsync();
        return Results.File(stream, "application/pdf");
    }

}
