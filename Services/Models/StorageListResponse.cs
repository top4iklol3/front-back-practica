namespace FileStorage.Services.Models;

public record StorageListResponse(string CurrentPath, IReadOnlyList<StorageItemResponse> Items);

