using System.Text.RegularExpressions;
using FileStorage.Options;
using FileStorage.Services.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FileStorage.Services;

public class StorageService : IStorageService
{
    private const long DefaultMaxUploadSize = 1_610_612_736; // ~1.5 GiB

    private readonly IWebHostEnvironment _environment;
    private readonly StorageOptions _storageOptions;
    private readonly FileIconOptions _iconOptions;
    private readonly ILogger<StorageService> _logger;
    private readonly string _absoluteBasePath;

    public StorageService(
        IWebHostEnvironment environment,
        IOptions<StorageOptions> storageOptions,
        IOptions<FileIconOptions> iconOptions,
        ILogger<StorageService> logger)
    {
        _environment = environment;
        _storageOptions = storageOptions.Value;
        _iconOptions = iconOptions.Value;
        _logger = logger;
        _absoluteBasePath = ResolveBasePath();
    }

    public Task<StorageListResponse> ListAsync(string resourceKey, string? path, CancellationToken cancellationToken)
    {
        var (resourceRoot, sanitizedResourceKey) = GetResourceRoot(resourceKey);
        var relativePath = NormalizePath(path);
        var absolutePath = CombineAbsolute(resourceRoot, relativePath);

        // Если запрашивается корзина (.trash) и она не существует, создаем её
        if (relativePath == ".trash" || relativePath?.StartsWith(".trash/") == true)
        {
            if (!Directory.Exists(absolutePath))
            {
                Directory.CreateDirectory(absolutePath);
            }
        }
        else if (!Directory.Exists(absolutePath))
        {
            throw new DirectoryNotFoundException("Папка не найдена.");
        }

        var directory = new DirectoryInfo(absolutePath);

        var dirs = directory.EnumerateDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => ToFolderResponse(relativePath, sanitizedResourceKey, d))
            .ToList();

        var files = directory.EnumerateFiles()
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => ToFileResponse(relativePath, sanitizedResourceKey, f))
            .ToList();

        var combined = dirs.Concat(files).ToList();
        return Task.FromResult(new StorageListResponse(relativePath, combined));
    }

    public async Task<UploadResponse> UploadAsync(string resourceKey, string? path, IFormFileCollection files, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("Файлы для загрузки не предоставлены.");
        }

        var (resourceRoot, sanitizedResourceKey) = GetResourceRoot(resourceKey);
        var relativePath = NormalizePath(path);
        var absoluteDirectory = CombineAbsolute(resourceRoot, relativePath);
        Directory.CreateDirectory(absoluteDirectory);

        var responses = new List<StorageItemResponse>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Length == 0)
            {
                continue;
            }

            if (file.Length > DefaultMaxUploadSize)
            {
                throw new InvalidOperationException($"Файл {file.FileName} превышает максимально допустимый размер 1.5 ГБ.");
            }

            var safeName = SanitizeName(file.FileName);
            var uniqueName = EnsureUniqueName(absoluteDirectory, safeName);
            var destinationPath = Path.Combine(absoluteDirectory, uniqueName);

            await using (var destinationStream = new FileStream(
                             destinationPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await file.CopyToAsync(destinationStream, cancellationToken);
            }

            responses.Add(ToFileResponse(relativePath, sanitizedResourceKey, new FileInfo(destinationPath)));
        }

        return new UploadResponse("Файлы успешно загружены", responses);
    }

    public Task<FileDownloadResult?> DownloadAsync(string resourceKey, string path, CancellationToken cancellationToken)
    {
        var (resourceRoot, _) = GetResourceRoot(resourceKey);
        var normalizedPath = NormalizePath(path, required: true);
        var absolutePath = CombineAbsolute(resourceRoot, normalizedPath);

        if (!System.IO.File.Exists(absolutePath))
        {
            return Task.FromResult<FileDownloadResult?>(null);
        }

        var fileInfo = new FileInfo(absolutePath);
        var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var contentType = GetContentType(fileInfo.Extension) ?? "application/octet-stream";
        return Task.FromResult<FileDownloadResult?>(new FileDownloadResult(stream, contentType, fileInfo.Name));
    }

    public Task<CreateFolderResponse> CreateFolderAsync(string resourceKey, string? path, string folderName, CancellationToken cancellationToken)
    {
        var (resourceRoot, sanitizedResourceKey) = GetResourceRoot(resourceKey);
        var relativePath = NormalizePath(path);
        var absoluteDirectory = CombineAbsolute(resourceRoot, relativePath);
        Directory.CreateDirectory(absoluteDirectory);

        var safeFolderName = SanitizeName(folderName);
        if (string.IsNullOrWhiteSpace(safeFolderName))
        {
            safeFolderName = "New Folder";
        }

        var uniqueFolderName = EnsureUniqueName(absoluteDirectory, safeFolderName);
        var newDirectoryPath = Path.Combine(absoluteDirectory, uniqueFolderName);
        Directory.CreateDirectory(newDirectoryPath);

        var response = ToFolderResponse(relativePath, sanitizedResourceKey, new DirectoryInfo(newDirectoryPath));
        return Task.FromResult(new CreateFolderResponse("Папка успешно создана", response));
    }

    public async Task<CreateUrlResponse> CreateUrlAsync(string resourceKey, string? path, string urlName, string url, CancellationToken cancellationToken)
    {
        var (resourceRoot, sanitizedResourceKey) = GetResourceRoot(resourceKey);
        var relativePath = NormalizePath(path);
        var absoluteDirectory = CombineAbsolute(resourceRoot, relativePath);
        Directory.CreateDirectory(absoluteDirectory);

        var safeName = SanitizeName(urlName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "New URL";
        }

        if (!safeName.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            safeName += ".url";
        }

        var uniqueName = EnsureUniqueName(absoluteDirectory, safeName);
        var urlFilePath = Path.Combine(absoluteDirectory, uniqueName);

        var urlContent = $"[InternetShortcut]\r\nURL={url}\r\n";
        await System.IO.File.WriteAllTextAsync(urlFilePath, urlContent, cancellationToken);

        var response = ToFileResponse(relativePath, sanitizedResourceKey, new FileInfo(urlFilePath));
        return new CreateUrlResponse("URL успешно создан", response);
    }

    public Task DeleteAsync(string resourceKey, string path, CancellationToken cancellationToken)
    {
        var (resourceRoot, _) = GetResourceRoot(resourceKey);
        var normalizedPath = NormalizePath(path, required: true);
        var absolutePath = CombineAbsolute(resourceRoot, normalizedPath);

        if (System.IO.File.Exists(absolutePath))
        {
            System.IO.File.Delete(absolutePath);
            return Task.CompletedTask;
        }

        if (Directory.Exists(absolutePath))
        {
            Directory.Delete(absolutePath, recursive: true);
            return Task.CompletedTask;
        }

        throw new FileNotFoundException("Элемент не найден.");
    }

    public Task MoveToTrashAsync(string resourceKey, string path, CancellationToken cancellationToken)
    {
        var (resourceRoot, _) = GetResourceRoot(resourceKey);
        var normalizedPath = NormalizePath(path, required: true);
        var absolutePath = CombineAbsolute(resourceRoot, normalizedPath);

        if (!System.IO.File.Exists(absolutePath) && !Directory.Exists(absolutePath))
        {
            throw new FileNotFoundException("Элемент не найден.");
        }

        // Создаем папку корзины
        var trashPath = Path.Combine(resourceRoot, ".trash");
        Directory.CreateDirectory(trashPath);

        // Получаем имя элемента
        var itemName = Path.GetFileName(absolutePath);
        if (string.IsNullOrEmpty(itemName))
        {
            itemName = "item";
        }

        // Генерируем уникальное имя для корзины (с timestamp)
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var uniqueName = $"{timestamp}_{itemName}";
        var destinationPath = Path.Combine(trashPath, uniqueName);

        // Если файл/папка с таким именем уже существует, добавляем счетчик
        var counter = 1;
        while (System.IO.File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(itemName);
            var ext = Path.GetExtension(itemName);
            uniqueName = $"{timestamp}_{nameWithoutExt}_{counter}{ext}";
            destinationPath = Path.Combine(trashPath, uniqueName);
            counter++;
        }

        // Перемещаем файл или папку
        if (System.IO.File.Exists(absolutePath))
        {
            System.IO.File.Move(absolutePath, destinationPath);
        }
        else if (Directory.Exists(absolutePath))
        {
            Directory.Move(absolutePath, destinationPath);
        }

        return Task.CompletedTask;
    }

    public Task RestoreFromTrashAsync(string resourceKey, string path, CancellationToken cancellationToken)
    {
        var (resourceRoot, _) = GetResourceRoot(resourceKey);
        var normalizedPath = NormalizePath(path, required: true);
        
        _logger.LogInformation("Восстановление из корзины: resourceKey={ResourceKey}, path={Path}, normalizedPath={NormalizedPath}", 
            resourceKey, path, normalizedPath);
        
        // Проверяем, что путь находится в корзине
        if (!normalizedPath.StartsWith(".trash/", StringComparison.Ordinal) && normalizedPath != ".trash")
        {
            _logger.LogWarning("Попытка восстановить элемент не из корзины: {Path}", normalizedPath);
            throw new InvalidOperationException("Можно восстанавливать только элементы из корзины.");
        }

        var absolutePath = CombineAbsolute(resourceRoot, normalizedPath);
        _logger.LogInformation("Абсолютный путь: {AbsolutePath}", absolutePath);

        if (!System.IO.File.Exists(absolutePath) && !Directory.Exists(absolutePath))
        {
            _logger.LogWarning("Элемент не найден по пути: {AbsolutePath}", absolutePath);
            throw new FileNotFoundException("Элемент не найден.");
        }

        // Получаем имя элемента (с timestamp)
        // Если путь содержит .trash/, берем только имя файла/папки (последнюю часть пути)
        string itemName;
        if (normalizedPath.StartsWith(".trash/", StringComparison.Ordinal))
        {
            // Берем последнюю часть пути после .trash/
            var pathAfterTrash = normalizedPath.Substring(".trash/".Length);
            itemName = Path.GetFileName(pathAfterTrash);
        }
        else if (normalizedPath == ".trash")
        {
            _logger.LogError("Попытка восстановить саму папку корзины, что недопустимо");
            throw new InvalidOperationException("Нельзя восстановить саму папку корзины.");
        }
        else
        {
            itemName = Path.GetFileName(absolutePath);
        }
            
        if (string.IsNullOrEmpty(itemName))
        {
            _logger.LogError("Не удалось определить имя элемента из пути: {Path}", normalizedPath);
            throw new InvalidOperationException("Не удалось определить имя элемента.");
        }

        _logger.LogInformation("Имя элемента с timestamp: {ItemName}", itemName);

        // Удаляем timestamp из имени
        // Формат: yyyyMMdd_HHmmss_originalname или yyyyMMdd_HHmmss_originalname_counter.ext
        var nameWithoutTimestamp = itemName;
        var timestampPattern = @"^\d{8}_\d{6}_";
        if (System.Text.RegularExpressions.Regex.IsMatch(itemName, timestampPattern))
        {
            nameWithoutTimestamp = System.Text.RegularExpressions.Regex.Replace(itemName, timestampPattern, "");
            _logger.LogInformation("Имя после удаления timestamp: {NameWithoutTimestamp}", nameWithoutTimestamp);
        }
        else
        {
            _logger.LogWarning("Имя элемента не содержит timestamp в ожидаемом формате: {ItemName}", itemName);
        }

        // Восстанавливаем в корень хранилища
        var restorePath = Path.Combine(resourceRoot, nameWithoutTimestamp);

        // Если файл с таким именем уже существует, добавляем счетчик
        var counter = 1;
        var baseName = Path.GetFileNameWithoutExtension(nameWithoutTimestamp);
        var extension = Path.GetExtension(nameWithoutTimestamp);
        while (System.IO.File.Exists(restorePath) || Directory.Exists(restorePath))
        {
            nameWithoutTimestamp = $"{baseName} ({counter}){extension}";
            restorePath = Path.Combine(resourceRoot, nameWithoutTimestamp);
            counter++;
        }

        _logger.LogInformation("Восстановление в путь: {RestorePath}", restorePath);

        // Перемещаем файл или папку обратно
        try
        {
            if (System.IO.File.Exists(absolutePath))
            {
                System.IO.File.Move(absolutePath, restorePath);
                _logger.LogInformation("Файл успешно восстановлен: {AbsolutePath} -> {RestorePath}", absolutePath, restorePath);
            }
            else if (Directory.Exists(absolutePath))
            {
                Directory.Move(absolutePath, restorePath);
                _logger.LogInformation("Папка успешно восстановлена: {AbsolutePath} -> {RestorePath}", absolutePath, restorePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при перемещении элемента: {AbsolutePath} -> {RestorePath}", absolutePath, restorePath);
            throw;
        }

        return Task.CompletedTask;
    }

    private StorageItemResponse ToFolderResponse(string currentPath, string resourceFolder, DirectoryInfo dir)
    {
        var relative = CombineRelative(currentPath, dir.Name);
        return new StorageItemResponse(
            Type: 0,
            Filename: dir.Name,
            FilenameWithoutExtension: dir.Name,
            Path: relative,
            Icon: string.IsNullOrWhiteSpace(_iconOptions.Folder) ? "📁" : _iconOptions.Folder);
    }

    private StorageItemResponse ToFileResponse(string currentPath, string resourceFolder, FileInfo file)
    {
        var relative = CombineRelative(currentPath, file.Name);
        var extension = file.Extension.ToLowerInvariant();
        var isUrl = extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
        var typeCode = isUrl ? 2 : 1;
        var icon = ResolveIcon(extension, isUrl);

        return new StorageItemResponse(
            Type: typeCode,
            Filename: file.Name,
            FilenameWithoutExtension: Path.GetFileNameWithoutExtension(file.Name),
            Path: relative,
            Icon: icon);
    }

    private string ResolveIcon(string extension, bool isUrl)
    {
        if (isUrl)
        {
            return string.IsNullOrWhiteSpace(_iconOptions.Url) ? "🔗" : _iconOptions.Url;
        }

        if (!string.IsNullOrWhiteSpace(extension) &&
            _iconOptions.Extensions.TryGetValue(extension, out var icon))
        {
            return icon;
        }

        return string.IsNullOrWhiteSpace(_iconOptions.Default) ? "📄" : _iconOptions.Default;
    }

    private (string Root, string SanitizedKey) GetResourceRoot(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("Ключ ресурса не может быть пустым.", nameof(resourceKey));
        }

        var sanitized = SanitizeResourceKey(resourceKey);
        var root = Path.Combine(_absoluteBasePath, sanitized);
        Directory.CreateDirectory(root);
        return (root, sanitized);
    }

    private string NormalizePath(string? path, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (required)
            {
                throw new ArgumentException("Путь не может быть пустым.");
            }

            return string.Empty;
        }

        var sanitized = path.Replace("\\", "/").Trim();
        sanitized = sanitized.Trim('/');

        if (sanitized.Contains("..", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Недопустимый путь.");
        }

        return sanitized;
    }

    private string CombineAbsolute(string root, string relative)
    {
        return string.IsNullOrWhiteSpace(relative)
            ? root
            : Path.Combine(root, relative.Replace("/", Path.DirectorySeparatorChar.ToString()));
    }

    private string CombineRelative(string current, string child)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return child;
        }

        return $"{current.TrimEnd('/')}/{child}";
    }

    private string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }

    private string SanitizeResourceKey(string key)
    {
        var sanitized = Regex.Replace(key, @"[^a-zA-Z0-9-_]", "_");
        return string.IsNullOrWhiteSpace(sanitized) ? $"resource_{Guid.NewGuid():N}" : sanitized;
    }

    private string EnsureUniqueName(string directory, string desiredName)
    {
        var candidate = desiredName;
        var counter = 1;
        var baseName = Path.GetFileNameWithoutExtension(desiredName);
        var extension = Path.GetExtension(desiredName);

        while (System.IO.File.Exists(Path.Combine(directory, candidate)) ||
               Directory.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{baseName} ({counter}){extension}";
            counter++;
        }

        return candidate;
    }

    private string ResolveBasePath()
    {
        var basePath = string.IsNullOrWhiteSpace(_storageOptions.BasePath)
            ? "Storage"
            : _storageOptions.BasePath;

        if (!Path.IsPathRooted(basePath))
        {
            basePath = Path.Combine(_environment.ContentRootPath, basePath);
        }

        Directory.CreateDirectory(basePath);
        return basePath;
    }

    private static string? GetContentType(string extension)
    {
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".csv" => "text/csv",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            _ => null
        };
    }
}

