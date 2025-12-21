namespace watch_sec_backend;

public class SentimentEngine
{
    private static readonly string[] NegativeKeywords = { "hate", "quit", "steal", "leak", "stupid", "management", "competitor", "upload", "copy" };
    private static readonly string[] HighRiskKeywords = { "password", "secret", "confidential", "ssn", "key", "token" };

    public static (double Score, string RiskLevel, List<string> Flags) AnalyzeText(string text)
    {
        var lower = text.ToLower();
        var flags = new List<string>();
        double score = 0;

        foreach (var word in NegativeKeywords)
        {
            if (lower.Contains(word))
            {
                score += 10;
                flags.Add($"Negative: {word}");
            }
        }

        foreach (var word in HighRiskKeywords)
        {
            if (lower.Contains(word))
            {
                score += 25;
                flags.Add($"High Risk: {word}");
            }
        }

        // Typing aggression (simulated by length/caps)
        if (text.Length > 50 && text.ToUpper() == text)
        {
            score += 15;
            flags.Add("Aggressive Typing (All Caps)");
        }

        string riskLevel = score switch
        {
            >= 50 => "CRITICAL",
            >= 20 => "HIGH",
            >= 10 => "MEDIUM",
            _ => "LOW"
        };

        return (score, riskLevel, flags);
    }
}
