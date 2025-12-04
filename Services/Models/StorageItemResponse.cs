namespace FileStorage.Services.Models;

public record StorageItemResponse(
    int Type,
    string Filename,
    string FilenameWithoutExtension,
    string Path,
    string Icon
);

