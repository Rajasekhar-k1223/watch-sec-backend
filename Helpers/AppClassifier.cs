namespace watch_sec_backend.Helpers;

public static class AppClassifier
{
    private static readonly Dictionary<string, string> AppCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        { "code", "Productive" }, { "visual studio", "Productive" }, { "devenv", "Productive" },
        { "slack", "Productive" }, { "teams", "Productive" }, { "outlook", "Productive" },
        { "winword", "Productive" }, { "excel", "Productive" }, { "powerpnt", "Productive" },
        { "chrome", "Neutral" }, { "msedge", "Neutral" }, 
        { "spotify", "Unproductive" }, { "steam", "Unproductive" }, { "netflix", "Unproductive" },
        { "explorer", "Neutral" }, { "searchhost", "Neutral" }
    };

    public static string Classify(string processName, string windowTitle)
    {
        if (string.IsNullOrEmpty(processName)) return "Neutral";
        
        foreach (var kvp in AppCategories)
        {
            if (processName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)) return kvp.Value;
        }

        if (string.IsNullOrEmpty(windowTitle)) return "Neutral";

        if (windowTitle.Contains("YouTube", StringComparison.OrdinalIgnoreCase)) return "Unproductive";
        if (windowTitle.Contains("Facebook", StringComparison.OrdinalIgnoreCase)) return "Unproductive";
        if (windowTitle.Contains("Jira", StringComparison.OrdinalIgnoreCase)) return "Productive";

        return "Neutral";
    }
}
