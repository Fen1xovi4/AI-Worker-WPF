namespace WpfBrowserWorker.Helpers;

public static class StringExtensions
{
    /// <summary>Show first 10 chars then "…" — safe for display in UI fields.</summary>
    public static string MaskToken(this string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        return token.Length <= 10 ? token : token[..10] + "…";
    }
}
