using Microsoft.AspNetCore.Http;
using FileStorage.Services.Models;

namespace FileStorage.Services;

public interface IStorageService
{
    Task<StorageListResponse> ListAsync(string resourceKey, string? path, CancellationToken cancellationToken);

    Task<UploadResponse> UploadAsync(string resourceKey, string? path, IFormFileCollection files, CancellationToken cancellationToken);

    Task<FileDownloadResult?> DownloadAsync(string resourceKey, string path, CancellationToken cancellationToken);

    Task<CreateFolderResponse> CreateFolderAsync(string resourceKey, string? path, string folderName, CancellationToken cancellationToken);

    Task<CreateUrlResponse> CreateUrlAsync(string resourceKey, string? path, string urlName, string url, CancellationToken cancellationToken);

    Task DeleteAsync(string resourceKey, string path, CancellationToken cancellationToken);

    Task MoveToTrashAsync(string resourceKey, string path, CancellationToken cancellationToken);

    Task RestoreFromTrashAsync(string resourceKey, string path, CancellationToken cancellationToken);
}

