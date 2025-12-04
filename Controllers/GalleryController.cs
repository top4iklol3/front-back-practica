using FileStorage.Services;
using FileStorage.Services.Models;
using Microsoft.AspNetCore.Mvc;

namespace FileStorage.Controllers;

/// <summary>
/// API для работы с галереей фотографий из хранилища.
/// Фотографии организованы по годам и категориям в папках: gallery/{year}/{category}/
/// </summary>
[ApiController]
[Route("api/gallery")]
public class GalleryController : ControllerBase
{
    private readonly IStorageService _storageService;
    private readonly ILogger<GalleryController> _logger;
    private const string GalleryFolder = "gallery";

    // Поддерживаемые форматы изображений
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg"
    };

    // Поддерживаемые форматы документов
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    // Проверяет, является ли файл изображением или документом
    private static bool IsMediaFile(string filename)
    {
        var extension = Path.GetExtension(filename);
        // Приводим к нижнему регистру для сравнения, так как HashSet использует StringComparer.OrdinalIgnoreCase
        var normalizedExtension = extension.ToLowerInvariant();
        var result = ImageExtensions.Contains(normalizedExtension) || DocumentExtensions.Contains(normalizedExtension);
        return result;
    }

    public GalleryController(IStorageService storageService, ILogger<GalleryController> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает список всех годов, для которых есть фотографии в галерее.
    /// </summary>
    [HttpGet("years")]
    public async Task<IActionResult> GetYears(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _storageService.ListAsync(GalleryFolder, null, cancellationToken);
            
            Console.WriteLine($"[GALLERY] GetYears: найдено элементов в корне: {result.Items.Count}");
            Console.WriteLine($"[GALLERY] GetYears: CurrentPath = '{result.CurrentPath}'");
            foreach (var item in result.Items)
            {
                Console.WriteLine($"[GALLERY] GetYears: элемент: {item.Filename}, тип: {item.Type}, путь: {item.Path}");
            }
            
            // Фильтруем только папки (года) и сортируем по убыванию
            var years = result.Items
                .Where(item => item.Type == 0 && int.TryParse(item.Filename, out _))
                .Select(item => int.Parse(item.Filename))
                .OrderByDescending(year => year)
                .ToList();

            Console.WriteLine($"[GALLERY] GetYears: найдено годов: {years.Count}");
            return Ok(new { years });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении списка годов");
            Console.WriteLine($"[GALLERY] GetYears: ОШИБКА - {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Возвращает список фотографий для указанного года.
    /// Если год не указан, возвращает все фотографии из всех годов.
    /// Поддерживает структуру gallery/{year}/{category}/
    /// </summary>
    [HttpGet("photos")]
    public async Task<IActionResult> GetPhotos([FromQuery] int? year, CancellationToken cancellationToken)
    {
        try
        {
            if (year.HasValue)
            {
                // Получаем фотографии и PDF для конкретного года (из всех категорий)
                var yearPath = $"{year.Value}";
                var yearResult = await _storageService.ListAsync(GalleryFolder, yearPath, cancellationToken);
                var allPhotos = new List<object>();

                // Проверяем, есть ли подпапки-категории
                var hasCategories = yearResult.Items.Any(item => item.Type == 0);
                
                if (hasCategories)
                {
                    // Структура с категориями: gallery/{year}/{category}/
                    foreach (var categoryFolder in yearResult.Items.Where(item => item.Type == 0))
                    {
                        // categoryFolder.Path уже содержит полный путь от корня resourceKey (например "2011/футбол")
                        // Используем его напрямую
                        var categoryPath = categoryFolder.Path;
                        
                        try
                        {
                            var categoryResult = await _storageService.ListAsync(GalleryFolder, categoryPath, cancellationToken);
                            var photos = categoryResult.Items
                                .Where(item => item.Type == 1 && IsMediaFile(item.Filename))
                                .Select(item => new
                                {
                                    item.Filename,
                                    item.Path,
                                    item.FilenameWithoutExtension,
                                    Year = year.Value,
                                    Category = categoryFolder.Filename,
                                    // item.Path уже содержит полный путь от корня resourceKey
                                    Url = $"/api/resources/{GalleryFolder}/storage/download?path={Uri.EscapeDataString(item.Path)}",
                                    Type = IsImageFile(item.Filename) ? "image" : "pdf"
                                });
                            
                            allPhotos.AddRange(photos);
                        }
                        catch
                        {
                            // Игнорируем ошибки для отдельных категорий
                        }
                    }
                }
                else
                {
                    // Старая структура без категорий: gallery/{year}/
                    var photos = yearResult.Items
                        .Where(item => item.Type == 1 && IsMediaFile(item.Filename))
                        .Select(item => new
                        {
                            item.Filename,
                            item.Path,
                            item.FilenameWithoutExtension,
                            Year = year.Value,
                            Category = (string?)null,
                            // item.Path уже содержит полный путь от корня resourceKey
                            Url = $"/api/resources/{GalleryFolder}/storage/download?path={Uri.EscapeDataString(item.Path)}",
                            Type = IsImageFile(item.Filename) ? "image" : "pdf"
                        });
                    
                    allPhotos.AddRange(photos);
                }

                return Ok(new { photos = allPhotos, year = year.Value });
            }
            else
            {
                // Получаем все фотографии из всех годов
                var rootResult = await _storageService.ListAsync(GalleryFolder, null, cancellationToken);
                var allPhotos = new List<object>();

                // Проходим по всем папкам-годам
                foreach (var yearFolder in rootResult.Items.Where(item => item.Type == 0 && int.TryParse(item.Filename, out _)))
                {
                    var folderYear = int.Parse(yearFolder.Filename);
                    var yearPath = string.IsNullOrEmpty(rootResult.CurrentPath) 
                        ? yearFolder.Filename 
                        : yearFolder.Path;
                    
                    try
                    {
                        var yearResult = await _storageService.ListAsync(GalleryFolder, yearPath, cancellationToken);
                        var hasCategories = yearResult.Items.Any(item => item.Type == 0);
                        
                        if (hasCategories)
                        {
                            // Структура с категориями
                            foreach (var categoryFolder in yearResult.Items.Where(item => item.Type == 0))
                            {
                                // categoryFolder.Path уже содержит полный путь от корня resourceKey (например "2011/футбол")
                                // Используем его напрямую
                                var categoryPath = categoryFolder.Path;
                                
                                try
                                {
                                    var categoryResult = await _storageService.ListAsync(GalleryFolder, categoryPath, cancellationToken);
                                    var photos = categoryResult.Items
                                        .Where(item => item.Type == 1 && IsMediaFile(item.Filename))
                                        .Select(item => new
                                        {
                                            item.Filename,
                                            item.Path,
                                            item.FilenameWithoutExtension,
                                            Year = folderYear,
                                            Category = categoryFolder.Filename,
                                            // item.Path уже содержит полный путь от корня resourceKey
                                            Url = $"/api/resources/{GalleryFolder}/storage/download?path={Uri.EscapeDataString(item.Path)}",
                                            Type = IsImageFile(item.Filename) ? "image" : "pdf"
                                        });
                                    
                                    allPhotos.AddRange(photos);
                                }
                                catch
                                {
                                    // Игнорируем ошибки
                                }
                            }
                        }
                        else
                        {
                            // Старая структура без категорий
                            var photos = yearResult.Items
                                .Where(item => item.Type == 1 && IsMediaFile(item.Filename))
                                .Select(item => new
                                {
                                    item.Filename,
                                    item.Path,
                                    item.FilenameWithoutExtension,
                                    Year = folderYear,
                                    Category = (string?)null,
                                    // item.Path уже содержит полный путь от корня resourceKey
                                    Url = $"/api/resources/{GalleryFolder}/storage/download?path={Uri.EscapeDataString(item.Path)}",
                                    Type = IsImageFile(item.Filename) ? "image" : "pdf"
                                });
                            
                            allPhotos.AddRange(photos);
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибки для отдельных годов
                    }
                }

                return Ok(new { photos = allPhotos });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении фотографий");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Проверяет наличие фотографий для указанного года.
    /// Возвращает true, если есть хотя бы одна фотография.
    /// </summary>
    [HttpGet("has-photos")]
    public async Task<IActionResult> HasPhotos([FromQuery] int year, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _storageService.ListAsync(GalleryFolder, $"{year}", cancellationToken);
            var hasPhotos = result.Items.Any(item => item.Type == 1 && IsMediaFile(item.Filename));
            return Ok(new { hasPhotos, year });
        }
        catch (DirectoryNotFoundException)
        {
            // Папка не существует - фотографий нет
            return Ok(new { hasPhotos = false, year });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при проверке наличия фотографий");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Возвращает список годов, для которых есть фотографии.
    /// Поддерживает структуру gallery/{year}/{category}/
    /// </summary>
    [HttpGet("years-with-photos")]
    public async Task<IActionResult> GetYearsWithPhotos(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _storageService.ListAsync(GalleryFolder, null, cancellationToken);
            var yearsWithPhotos = new List<int>();

            // Проходим по всем папкам-годам и проверяем наличие фотографий
            foreach (var yearFolder in result.Items.Where(item => item.Type == 0 && int.TryParse(item.Filename, out _)))
            {
                var year = int.Parse(yearFolder.Filename);
                var yearPath = string.IsNullOrEmpty(result.CurrentPath) 
                    ? yearFolder.Filename 
                    : yearFolder.Path;
                
                try
                {
                    var yearResult = await _storageService.ListAsync(GalleryFolder, yearPath, cancellationToken);
                    var hasPhotos = false;
                    
                    // Проверяем, есть ли подпапки-категории
                    var hasCategories = yearResult.Items.Any(item => item.Type == 0);
                    
                    if (hasCategories)
                    {
                        // Проверяем наличие фотографий в категориях
                        foreach (var categoryFolder in yearResult.Items.Where(item => item.Type == 0))
                        {
                            // categoryFolder.Path уже содержит полный путь от корня resourceKey
                            // Используем его напрямую
                            var categoryPath = categoryFolder.Path;
                            
                            try
                            {
                                var categoryResult = await _storageService.ListAsync(GalleryFolder, categoryPath, cancellationToken);
                                if (categoryResult.Items.Any(item => item.Type == 1 && IsMediaFile(item.Filename)))
                                {
                                    hasPhotos = true;
                                    break;
                                }
                            }
                            catch
                            {
                                // Игнорируем ошибки
                            }
                        }
                    }
                    else
                    {
                        // Старая структура без категорий
                        hasPhotos = yearResult.Items.Any(item => item.Type == 1 && IsMediaFile(item.Filename));
                    }
                    
                    if (hasPhotos)
                    {
                        yearsWithPhotos.Add(year);
                    }
                }
                catch
                {
                    // Игнорируем ошибки для отдельных папок
                }
            }

            return Ok(new { years = yearsWithPhotos.OrderByDescending(y => y).ToList() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении годов с фотографиями");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static bool IsImageFile(string filename)
    {
        var extension = Path.GetExtension(filename);
        return ImageExtensions.Contains(extension);
    }

    /// <summary>
    /// Возвращает список карточек (категорий) для указанного года.
    /// Каждая карточка содержит название, год, категорию и количество фотографий.
    /// </summary>
    [HttpGet("cards")]
    public async Task<IActionResult> GetCards([FromQuery] int? year, CancellationToken cancellationToken)
    {
        try
        {
            var cards = new List<object>();

            if (year.HasValue)
            {
                // Получаем карточки для конкретного года
                var yearPath = $"{year.Value}";
                _logger.LogWarning("DEBUG: Загрузка карточек для года {Year}, путь: {Path}", year.Value, yearPath);
                var yearResult = await _storageService.ListAsync(GalleryFolder, yearPath, cancellationToken);
                _logger.LogWarning("DEBUG: Найдено элементов в году {Year}: {Count}", year.Value, yearResult.Items.Count);
                foreach (var item in yearResult.Items)
                {
                    _logger.LogWarning("DEBUG: Элемент в году {Year}: {Name}, тип: {Type}", year.Value, item.Filename, item.Type);
                }
                
                // Каждая папка-категория - это карточка
                foreach (var categoryFolder in yearResult.Items.Where(item => item.Type == 0))
                {
                    // categoryFolder.Path уже содержит полный путь от корня resourceKey (например "2011/футбол")
                    // Используем его напрямую
                    var categoryPath = categoryFolder.Path;
                    
                    _logger.LogInformation("Проверка категории: {Category}, путь: {Path}", categoryFolder.Filename, categoryPath);
                    
                    try
                    {
                        var categoryResult = await _storageService.ListAsync(GalleryFolder, categoryPath, cancellationToken);
                        _logger.LogWarning("DEBUG: Категория {Category}, путь: {Path}, всего элементов: {Total}", 
                            categoryFolder.Filename, categoryPath, categoryResult.Items.Count);
                        
                        // Логируем все элементы в категории
                        foreach (var item in categoryResult.Items)
                        {
                            _logger.LogWarning("DEBUG: Элемент: {Name}, тип: {Type}, расширение: {Ext}, IsMediaFile: {IsMedia}", 
                                item.Filename, item.Type, Path.GetExtension(item.Filename), IsMediaFile(item.Filename));
                        }
                        
                        var photoCount = categoryResult.Items.Count(item => item.Type == 1 && IsMediaFile(item.Filename));
                        _logger.LogWarning("DEBUG: Категория {Category}: найдено {Count} медиафайлов", categoryFolder.Filename, photoCount);
                        
                        if (photoCount > 0)
                        {
                            // Получаем первое изображение для превью
                            var firstImage = categoryResult.Items
                                .FirstOrDefault(item => item.Type == 1 && IsImageFile(item.Filename));
                            
                            // firstImage.Path уже содержит полный путь от корня resourceKey
                            var thumbnailUrl = firstImage != null
                                ? $"/api/resources/{GalleryFolder}/storage/download?path={Uri.EscapeDataString(firstImage.Path)}"
                                : null;

                            cards.Add(new
                            {
                                id = $"{year.Value}_{categoryFolder.Filename}",
                                name = categoryFolder.Filename,
                                year = year.Value,
                                category = categoryFolder.Filename,
                                photoCount = photoCount,
                                thumbnail = thumbnailUrl
                            });
                            _logger.LogInformation("Добавлена карточка: {Name}, год: {Year}, фото: {Count}", categoryFolder.Filename, year.Value, photoCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Ошибка при обработке категории {Category}", categoryFolder.Filename);
                    }
                }
            }
            else
            {
                // Получаем карточки для всех годов
                Console.WriteLine("[GALLERY] Загрузка карточек для всех годов");
                _logger.LogWarning("DEBUG: Загрузка карточек для всех годов");
                var rootResult = await _storageService.ListAsync(GalleryFolder, null, cancellationToken);
                Console.WriteLine($"[GALLERY] Найдено элементов в корне галереи: {rootResult.Items.Count}");
                Console.WriteLine($"[GALLERY] CurrentPath корня: '{rootResult.CurrentPath}'");
                _logger.LogWarning("DEBUG: Найдено элементов в корне галереи: {Count}", rootResult.Items.Count);
                foreach (var item in rootResult.Items)
                {
                    Console.WriteLine($"[GALLERY] Элемент в корне: {item.Filename}, тип: {item.Type}, путь: {item.Path}");
                    _logger.LogWarning("DEBUG: Элемент в корне: {Name}, тип: {Type}", item.Filename, item.Type);
                }
                
                foreach (var yearFolder in rootResult.Items.Where(item => item.Type == 0 && int.TryParse(item.Filename, out _)))
                {
                    var folderYear = int.Parse(yearFolder.Filename);
                    // yearFolder.Path уже содержит путь от корня (например "2011")
                    var yearPath = yearFolder.Path;
                    
                    Console.WriteLine($"[GALLERY] Обработка года {folderYear}, путь: {yearPath}");
                    _logger.LogInformation("Обработка года {Year}, путь: {Path}", folderYear, yearPath);
                    
                    try
                    {
                        var yearResult = await _storageService.ListAsync(GalleryFolder, yearPath, cancellationToken);
                        Console.WriteLine($"[GALLERY] В году {folderYear} найдено элементов: {yearResult.Items.Count}");
                        Console.WriteLine($"[GALLERY] CurrentPath года {folderYear}: '{yearResult.CurrentPath}'");
                        _logger.LogInformation("В году {Year} найдено элементов: {Count}", folderYear, yearResult.Items.Count);
                        
                        foreach (var categoryFolder in yearResult.Items.Where(item => item.Type == 0))
                        {
                            // categoryFolder.Path уже содержит полный путь от корня resourceKey (например "2011/футбол")
                            // Используем его напрямую
                            var categoryPath = categoryFolder.Path;
                            
                            _logger.LogInformation("Проверка категории: {Category}, путь: {Path}", categoryFolder.Filename, categoryPath);
                            
                            try
                            {
                                var categoryResult = await _storageService.ListAsync(GalleryFolder, categoryPath, cancellationToken);
                                _logger.LogWarning("DEBUG: Категория {Category}, путь: {Path}, всего элементов: {Total}", 
                                    categoryFolder.Filename, categoryPath, categoryResult.Items.Count);
                                
                                // Логируем все элементы в категории
                                foreach (var item in categoryResult.Items)
                                {
                                    _logger.LogWarning("DEBUG: Элемент: {Name}, тип: {Type}, расширение: {Ext}, IsMediaFile: {IsMedia}", 
                                        item.Filename, item.Type, Path.GetExtension(item.Filename), IsMediaFile(item.Filename));
                                }
                                
                                var photoCount = categoryResult.Items.Count(item => item.Type == 1 && IsMediaFile(item.Filename));
                                _logger.LogWarning("DEBUG: Категория {Category}: найдено {Count} медиафайлов", categoryFolder.Filename, photoCount);
                                
                                if (photoCount > 0)
                                {
                                    var firstImage = categoryResult.Items
                                        .FirstOrDefault(item => item.Type == 1 && IsImageFile(item.Filename));
                                    
                                    // firstImage.Path уже содержит полный путь от корня resourceKey
                                    var thumbnailUrl = firstImage != null
                                        ? $"/api/resources/{GalleryFolder}/storage/download?path={Uri.EscapeDataString(firstImage.Path)}"
                                        : null;

                                    cards.Add(new
                                    {
                                        id = $"{folderYear}_{categoryFolder.Filename}",
                                        name = categoryFolder.Filename,
                                        year = folderYear,
                                        category = categoryFolder.Filename,
                                        photoCount = photoCount,
                                        thumbnail = thumbnailUrl
                                    });
                                    _logger.LogInformation("Добавлена карточка: {Name}, год: {Year}, фото: {Count}", categoryFolder.Filename, folderYear, photoCount);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Ошибка при обработке категории {Category}", categoryFolder.Filename);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Ошибка при обработке года {Year}", folderYear);
                    }
                }
            }

            // Всегда выводим в консоль для отладки
            Console.WriteLine($"[GALLERY] Всего найдено карточек: {cards.Count}");
            _logger.LogWarning("DEBUG: Всего найдено карточек: {Count}", cards.Count);
            if (cards.Count > 0)
            {
                var firstCardJson = System.Text.Json.JsonSerializer.Serialize(cards.First());
                Console.WriteLine($"[GALLERY] Первая карточка: {firstCardJson}");
                _logger.LogWarning("DEBUG: Первая карточка: {FirstCard}", firstCardJson);
            }
            else
            {
                Console.WriteLine("[GALLERY] ⚠️ Карточки не найдены!");
                Console.WriteLine("[GALLERY] Проверьте структуру: Storage/gallery/{{год}}/{{категория}}/");
                _logger.LogWarning("DEBUG: Карточки не найдены. Проверьте структуру: Storage/gallery/{{год}}/{{категория}}/");
            }
            return Ok(new { cards });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении карточек");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Возвращает все фотографии для конкретной карточки (категории).
    /// </summary>
    [HttpGet("card-photos")]
    public async Task<IActionResult> GetCardPhotos([FromQuery] int year, [FromQuery] string category, CancellationToken cancellationToken)
    {
        try
        {
            var categoryPath = $"{year}/{category}";
            var result = await _storageService.ListAsync(GalleryFolder, categoryPath, cancellationToken);
            
            var photos = result.Items
                .Where(item => item.Type == 1 && IsMediaFile(item.Filename))
                .Select(item => new
                {
                    item.Filename,
                    item.Path,
                    item.FilenameWithoutExtension,
                    Year = year,
                    Category = category,
                    // item.Path уже содержит полный путь от корня resourceKey (например "2011/футбол/photo.jpg")
                    Url = $"/api/resources/{GalleryFolder}/storage/download?path={Uri.EscapeDataString(item.Path)}",
                    Type = IsImageFile(item.Filename) ? "image" : "pdf"
                })
                .ToList();

            return Ok(new { photos, year, category });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении фотографий карточки");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

