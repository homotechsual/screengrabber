namespace Screengrabber.Api;

public static class ApiKeyAuth
{
    public static HashSet<string> ParseConfiguredKeys(string? rawValue)
        => (rawValue ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

    public static bool IsRequestAuthorized(HttpContext context, IReadOnlySet<string> validKeys)
    {
        var key = context.Request.Headers["X-Api-Key"].ToString();
        return ApiKeyMiddleware.IsAuthorized(string.IsNullOrEmpty(key) ? null : key, validKeys);
    }
}