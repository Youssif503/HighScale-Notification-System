using NotificationSystem.Providers;
using NotificationSystem.Services;
using NotificationSystem.Workers;
using NotificationSystem.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var provider = builder.Configuration["Database:Provider"] ?? "Postgres";
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(connectionString);
    else
        options.UseNpgsql(connectionString);
});
builder.Services.AddSingleton<NotificationIdempotencyStore>();
builder.Services.AddSingleton<INotificationPublisher, RabbitMqPublisher>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<INotificationProvider, EmailNotificationProvider>();

builder.Services.AddHostedService<NotificationWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.UseHttpsRedirection();

app.Run();

