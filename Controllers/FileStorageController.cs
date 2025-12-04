using System.IO;
using FileStorage.Services;
using FileStorage.Services.Models;
using Microsoft.AspNetCore.Mvc;

namespace FileStorage.Controllers;

/// <summary>
/// API для работы с файловым хранилищем конкретного ресурса (tenant).
/// Контроллер содержит только координационную логику, вся работа с ФС вынесена в сервис.
/// </summary>
[ApiController]
[Route("api/resources/{resourceKey}/storage")]
public class FileStorageController : ControllerBase
{
    private readonly IStorageService _storageService;
    private readonly ILogger<FileStorageController> _logger;

    public FileStorageController(IStorageService storageService, ILogger<FileStorageController> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает список папок/файлов/URL внутри хранилища.
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> GetItems([FromRoute] string resourceKey, [FromQuery] string? path, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _storageService.ListAsync(resourceKey, path, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении списка файлов");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Потоковая загрузка файлов в выбранную папку.
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromRoute] string resourceKey, [FromQuery] string? path, CancellationToken cancellationToken)
    {
        try
        {
            if (!Request.HasFormContentType || Request.Form.Files.Count == 0)
            {
                return BadRequest(new { error = "Файлы для загрузки не найдены." });
            }

            var result = await _storageService.UploadAsync(resourceKey, path, Request.Form.Files, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке файлов");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Потоковая выдача файла по относительному пути.
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download([FromRoute] string resourceKey, [FromQuery] string path, CancellationToken cancellationToken)
    {
        try
        {
            var download = await _storageService.DownloadAsync(resourceKey, path, cancellationToken);
            if (download is null)
            {
                return NotFound(new { error = "Файл не найден." });
            }

            return File(download.Content, download.ContentType, download.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при скачивании файла");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Создает папку в указанном пути.
    /// </summary>
    [HttpPost("folder")]
    public async Task<IActionResult> CreateFolder([FromRoute] string resourceKey, [FromQuery] string? path, [FromBody] CreateFolderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Название папки обязательно." });
            }

            var result = await _storageService.CreateFolderAsync(resourceKey, path, request.Name, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании папки");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Создает .url файл (ярлык на внешний ресурс).
    /// </summary>
    [HttpPost("url")]
    public async Task<IActionResult> CreateUrl([FromRoute] string resourceKey, [FromQuery] string? path, [FromBody] CreateUrlRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Название URL обязательно." });
            }

            if (string.IsNullOrWhiteSpace(request.Url))
            {
                return BadRequest(new { error = "URL адрес обязателен." });
            }

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest(new { error = "Некорректный URL. Используйте http:// или https://." });
            }

            var result = await _storageService.CreateUrlAsync(resourceKey, path, request.Name, request.Url, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании URL");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Удаляет файл или папку (рекурсивно).
    /// </summary>
    [HttpDelete("item")]
    public async Task<IActionResult> Delete([FromRoute] string resourceKey, [FromQuery] string path, CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.DeleteAsync(resourceKey, path, cancellationToken);
            return Ok(new { message = "Элемент успешно удален." });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "Элемент не найден." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при удалении элемента");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Перемещает файл или папку в корзину.
    /// </summary>
    [HttpPost("trash")]
    public async Task<IActionResult> MoveToTrash([FromRoute] string resourceKey, [FromQuery] string path, CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.MoveToTrashAsync(resourceKey, path, cancellationToken);
            return Ok(new { message = "Элемент перемещен в корзину." });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "Элемент не найден." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при перемещении в корзину");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Восстанавливает файл или папку из корзины.
    /// </summary>
    [HttpPost("restore")]
    public async Task<IActionResult> RestoreFromTrash([FromRoute] string resourceKey, [FromQuery] string path, CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.RestoreFromTrashAsync(resourceKey, path, cancellationToken);
            return Ok(new { message = "Элемент успешно восстановлен." });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "Элемент не найден." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при восстановлении из корзины");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class CreateFolderRequest
{
    public string Name { get; set; } = string.Empty;
}

public class CreateUrlRequest
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

