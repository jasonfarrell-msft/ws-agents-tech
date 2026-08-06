using ExecutiveDashboard.Services;

var builder = WebApplication.CreateBuilder(args);
var startupState = builder.AddExecutiveDashboardServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseExecutiveDashboard(startupState);

app.Run();
