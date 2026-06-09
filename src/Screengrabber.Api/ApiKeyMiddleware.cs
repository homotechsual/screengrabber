namespace Screengrabber.Api;

public static class ApiKeyMiddleware
{
    public static bool IsAuthorized(string? providedKey, IReadOnlySet<string> validKeys)
    {
        if (validKeys.Count == 0) return true;
        return !string.IsNullOrEmpty(providedKey) && validKeys.Contains(providedKey);
    }
}
