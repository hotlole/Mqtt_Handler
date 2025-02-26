using BlazorApp2.Components;
using BlazorApp2.Service;

var builder = WebApplication.CreateBuilder(args);

// Убедитесь, что логирование настроено правильно
builder.Logging.AddConsole();  // Добавляем консольный логерё
// Добавляем необходимые сервисы
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<MqttService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();  // Активируем HSTS для безопасных соединений
}

app.UseHttpsRedirection();
app.UseStaticFiles();  // Для статических файлов
app.UseAntiforgery();  // Для защиты от CSRF

// Находим точку, где происходит рендеринг Razor-компонентов
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
