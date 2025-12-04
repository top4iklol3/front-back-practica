namespace FileStorage.Services.Models;

public sealed class FileDownloadResult : IDisposable
{
    public FileDownloadResult(Stream content, string contentType, string fileName)
    {
        Content = content;
        ContentType = contentType;
        FileName = fileName;
    }

    public Stream Content { get; }

    public string ContentType { get; }

    public string FileName { get; }

    public void Dispose()
    {
        Content.Dispose();
    }
}

