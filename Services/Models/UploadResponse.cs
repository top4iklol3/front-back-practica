namespace FileStorage.Services.Models;

public record UploadResponse(string Message, IReadOnlyList<StorageItemResponse> Files);

