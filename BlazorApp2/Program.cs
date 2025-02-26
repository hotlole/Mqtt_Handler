using BlazorApp2.Components;
using BlazorApp2.Service;

var builder = WebApplication.CreateBuilder(args);

// ���������, ��� ����������� ��������� ���������
builder.Logging.AddConsole();  // ��������� ���������� �����
// ��������� ����������� �������
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<MqttService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();  // ���������� HSTS ��� ���������� ����������
}

app.UseHttpsRedirection();
app.UseStaticFiles();  // ��� ����������� ������
app.UseAntiforgery();  // ��� ������ �� CSRF

// ������� �����, ��� ���������� ��������� Razor-�����������
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
