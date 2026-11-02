using L1bre.Journal.Components;
using L1bre.Journal.Components.Account;
using L1bre.Journal.CTrader;
using L1bre.Journal.Data;
using L1bre.Journal.Domain.Accounts;
using L1bre.Journal.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin))
    .AddPolicy(Roles.Trader, policy => policy.RequireRole(Roles.Trader));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Login cookies are signed with keys stored in the database, so a restart or an update doesn't log everyone out.
builder.Services.AddDataProtection()
    .SetApplicationName("L1bre.Journal")
    .PersistKeysToDbContext<ApplicationDbContext>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<InvitationService>();

// cTrader Open API: client ID and secret come from the environment (CTrader__ClientId, CTrader__ClientSecret),
// never from a file in Git. Without them the app runs, and the accounts page says linking isn't set up.
builder.Services.Configure<CTraderOptions>(builder.Configuration.GetSection(CTraderOptions.Section));
builder.Services.AddHttpClient<CTraderApi>();
builder.Services.AddScoped<CTraderTokenProtector>();
builder.Services.AddScoped<CTraderLinkService>();
builder.Services.AddHostedService<TokenRenewalWorker>();
builder.Services.AddSingleton<SyncSignal>();
builder.Services.AddScoped<TradeSyncService>();
builder.Services.AddScoped<TradeQueries>();
builder.Services.AddScoped<TradeService>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();

// Caddy terminates HTTPS and forwards plain HTTP to the app inside the Docker network.
// Trust its X-Forwarded-* headers so the app knows the original scheme (https) and client IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // The app port is only reachable from the Docker network, so any proxy there is ours.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();
app.MapCTraderEndpoints();
app.MapHealthChecks("/healthz");

await StartupTasks.RunAsync(app.Services, app.Configuration);

app.Run();
