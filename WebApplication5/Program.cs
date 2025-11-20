using System.Net.NetworkInformation;
using System.Net.Sockets;
using WebApplication5.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.Name = ".MonitoringApp.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Resolve connection string from configuration
string? connectionString = builder.Configuration.GetConnectionString("MonitoringApp");

// Fallback to explicit configuration key if necessary
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = builder.Configuration["ConnectionStrings:MonitoringApp"];
}

// Fallback to environment variable
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable("MONITORINGAPP_CONNECTIONSTRING");
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'MonitoringApp' not found. Add it to appsettings.json under ConnectionStrings or set the MONITORINGAPP_CONNECTIONSTRING environment variable.");
}

string GetLocalIPv4()
{
    try
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                 n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var ipProps = ni.GetIPProperties();
            foreach (var ua in ipProps.UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var ip = ua.Address.ToString();
                    // Skip link-local addresses
                    if (!ip.StartsWith("169.254")) return ip;
                }
            }
        }
    }
    catch
    {
        // ignore and fallback
    }

    return "localhost";
}

var localIp = GetLocalIPv4();

// Construct URLs to bind to the detected IP.
// Adjust ports to match your launchSettings.json or desired ports.
var httpPort = 5293;
var httpsPort = 7011;
var urls = $"http://{localIp}:{httpPort};https://{localIp}:{httpsPort}";

// Optionally, only override if ASPNETCORE_URLS is not explicitly set.
// If you always want to force the system IP, omit the check below.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls(urls);
}
else
{
    // If ASPNETCORE_URLS exists but contains "localhost", replace it with detected IP.
    var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (envUrls != null && envUrls.Contains("localhost", StringComparison.OrdinalIgnoreCase))
    {
        var replaced = envUrls.Replace("localhost", localIp, StringComparison.OrdinalIgnoreCase);
        builder.WebHost.UseUrls(replaced);
    }
}



var app = builder.Build();

DBItemManagement dbManager = new DBItemManagement();
dbManager.SetConnectionString(connectionString);
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

