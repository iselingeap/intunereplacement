using System.Data.Common;
using WebApplication5.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor(); 
builder.Services.AddSession();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});


var app = builder.Build();

DBItemManagement dbManager = new DBItemManagement();
dbManager.SetConnectionString("Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=MonitoringApp;User ID=johnny;Password=test");
dbManager.TestConnection();

app.UseSession();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseSession();

// Global middleware to block access until user is logged in
app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    // Paths/prefixes that should be allowed without authentication
    var allowedPrefixes = new[]
    {
        "/",
        "/Index",
        "/Index/",
        "/favicon.ico",
        "/css",
        "/js",
        "/lib",
        "/images",
        "/_framework",
        "/_content",
        "/_blazor" // in case Blazor assets exist
    };

    bool isAllowed = allowedPrefixes.Any(p =>
        path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    // Allow requests that are not for HTML pages (e.g., static files already covered),
    // and allow the login page (Index).
    if (isAllowed)
    {
        await next();
        return;
    }

    // Allow health checks or other explicit endpoints if needed by adding their prefixes above.

    // Check session for IsLoggedIn flag
    var isLoggedIn = context.Session.GetString("IsLoggedIn") == "true";

    if (!isLoggedIn)
    {
        // If it's an AJAX/fetch/XHR call, return 401 to allow client-side handling
        if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
            context.Request.Headers.Accept.Any(h => h.Contains("application/json")))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        // Redirect unauthenticated requests to login page
        // Preserve original path via query string if desired: ?returnUrl=...
        var loginUrl = "/"; // Index page is the login page
        var returnUrl = context.Request.Path + context.Request.QueryString;
        loginUrl = string.IsNullOrEmpty(returnUrl) ? loginUrl : $"{loginUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}";

        context.Response.Redirect(loginUrl);
        return;
    }

    await next();
});

app.MapRazorPages();

app.Run();
