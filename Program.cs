using FileStorage.Options;
using FileStorage.Services;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<FileIconOptions>(builder.Configuration.GetSection("FileIcons"));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection("Cors"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IStorageService, StorageService>();

// Configure upload limits (1.5 GiB)
const long maxUploadSize = 1_610_612_736;
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadSize;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadSize;
});

// Configure CORS from appsettings - только для разрешенных ресурсов
var corsOptions = builder.Configuration.GetSection("Cors").Get<CorsOptions>();
var allowedOrigins = corsOptions?.AllowedOrigins ?? Array.Empty<string>();
var corsConfigured = allowedOrigins.Length > 0;

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredCors", policy =>
    {
        if (corsConfigured)
        {
            policy.WithOrigins(allowedOrigins);
            
            // Настройка заголовков
            if (corsOptions!.AllowedHeaders.Length > 0)
            {
                policy.WithHeaders(corsOptions.AllowedHeaders);
            }
            else
            {
                policy.AllowAnyHeader();
            }
            
            // Настройка методов
            if (corsOptions.AllowedMethods.Length > 0)
            {
                policy.WithMethods(corsOptions.AllowedMethods);
            }
            else
            {
                policy.AllowAnyMethod();
            }
            
            // Настройка credentials
            if (corsOptions.AllowCredentials)
            {
                policy.AllowCredentials();
            }
        }
        // Если origins не указаны - CORS блокирует все запросы (безопасно)
    });
});

var app = builder.Build();

if (!corsConfigured)
{
    app.Logger.LogWarning("CORS origins are not configured. Cross-origin requests will be blocked.");
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "File Storage API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("ConfiguredCors");

app.UseStaticFiles(); // Включаем статические файлы для тестовой страницы

app.UseAuthorization();
app.MapControllers();

// Для теста показываем index.html вместо редиректа
app.MapFallbackToFile("index.html");

app.Run();

