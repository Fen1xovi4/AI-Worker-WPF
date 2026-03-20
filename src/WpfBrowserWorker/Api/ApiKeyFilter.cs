using Microsoft.AspNetCore.Http;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Api;

/// <summary>
/// Проверяет заголовок X-Api-Key на всех /api/* запросах.
/// </summary>
public class ApiKeyFilter(WorkerConfig config) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            return await next(ctx); // ключ не настроен — пропускаем (localhost dev)

        if (!ctx.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var key)
            || key != config.ApiKey)
        {
            return Results.Json(new { error = "Unauthorized" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(ctx);
    }
}
