namespace FileStorage.Options;

public class FileIconOptions
{
    public string Default { get; set; } = "📄";
    public string Folder { get; set; } = "📁";
    public string Url { get; set; } = "🔗";
    public Dictionary<string, string> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

