using MySqlConnector;
using SpazaSnap;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<StockStore>();
builder.Services.AddScoped<AccountStore>();
var storagePath=Path.GetFullPath(builder.Configuration["Storage:Path"] ?? Path.Combine(builder.Environment.ContentRootPath,"App_Data"));
Directory.CreateDirectory(Path.Combine(storagePath,"keys"));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storagePath,"keys"))).SetApplicationName("SpazaSnap");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o=>
{
    o.Cookie.Name="SpazaSnap.Session";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;
    o.Cookie.SecurePolicy=builder.Environment.IsDevelopment()?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;
    o.ExpireTimeSpan=TimeSpan.FromHours(8);o.SlidingExpiration=true;
    o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return Task.CompletedTask;};
    o.Events.OnRedirectToAccessDenied=c=>{c.Response.StatusCode=403;return Task.CompletedTask;};
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o=>
{
    o.RejectionStatusCode=429;
    o.AddPolicy("account",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=15,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
builder.Services.Configure<ForwardedHeadersOptions>(o=>
{
    o.ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto;
    if(System.Net.IPAddress.TryParse(builder.Configuration["Deployment:TrustedProxy"],out var ip))o.KnownProxies.Add(ip);
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 8 * 1024 * 1024);
var app = builder.Build();
if (args.Contains("--init-db"))
{
    using var scope=app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<StockStore>().InitializeAsync();
    Console.WriteLine("SaaS database ready. Shops and optional sample products are created at registration.");
    return;
}
app.UseForwardedHeaders();
if(!app.Environment.IsDevelopment())app.UseHsts();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"]="same-origin";
    context.Response.Headers["X-Frame-Options"]="DENY";
    if(context.Request.Path.StartsWithSegments("/api"))context.Response.Headers.CacheControl="no-store";
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        if(context.Request.Headers["X-SpazaSnap"]!="1" || (context.Request.Headers.Origin.FirstOrDefault() is { } origin && origin != $"{context.Request.Scheme}://{context.Request.Host}"))
        { context.Response.StatusCode = 403; return; }
    }
    try { await next(); }
    catch (ArgumentException e) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = e.Message }); }
    catch (KeyNotFoundException e) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new { error = e.Message }); }
    catch (UnauthorizedAccessException e) { context.Response.StatusCode=401; await context.Response.WriteAsJsonAsync(new {error=e.Message}); }
    catch (MySqlException e)
    {
        app.Logger.LogError(e, "Database request failed"); context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = "MySQL is unavailable or the schema is missing. Check your connection and run --init-db." });
    }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
app.MapGet("/api/health", async (StockStore db) => { await db.PingAsync(); return Results.Ok(new { status = "ready", database = "MySQL" }); });
async Task<IResult> SignIn(HttpContext ctx,Account account)
{
    var claims=new[]{new Claim(ClaimTypes.NameIdentifier,account.UserId.ToString()),new Claim("shop_id",account.ShopId.ToString()),new Claim(ClaimTypes.Name,account.ShopName)};
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(account);
}
app.MapPost("/api/account/register",async(RegisterInput input,AccountStore accounts,HttpContext ctx)=>await SignIn(ctx,await accounts.RegisterAsync(input))).RequireRateLimiting("account");
app.MapPost("/api/account/login",async(LoginInput input,AccountStore accounts,HttpContext ctx)=>
{
    var account=await accounts.LoginAsync(input);
    return account is null?Results.Json(new {error="Email or password is incorrect, or the account is temporarily locked. Try again after 15 minutes."},statusCode:401):await SignIn(ctx,account);
}).RequireRateLimiting("account");
var api=app.MapGroup("/api").RequireAuthorization();
api.MapGet("/account/session",async(AccountStore accounts,HttpContext ctx)=>
{
    var account=await accounts.FindAsync(int.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
    if(account is null){await ctx.SignOutAsync();return Results.Unauthorized();}
    return Results.Ok(account);
});
api.MapPost("/account/logout",async(HttpContext ctx)=>{await ctx.SignOutAsync();return Results.Ok();});
api.MapGet("/account/plan",async(StockStore db)=>new {name="Free",productLimit=100,photoLimit=1000,productsUsed=(await db.ProductsAsync()).Count,billing="No paid subscription configured"});
api.MapGet("/products", (StockStore db) => db.ProductsAsync());
api.MapPost("/products", async (ProductInput input, StockStore db) => Results.Ok(await db.AddProductAsync(input)));
api.MapPost("/products/{id:int}/adjust", async (int id, Adjustment input, StockStore db) => { await db.AdjustAsync(id, input); return Results.Ok(); });
api.MapGet("/restocks", (StockStore db) => db.RestocksAsync());
api.MapGet("/orders", (StockStore db) => db.OrdersAsync());
api.MapPost("/orders", async (OrderInput input, StockStore db) => Results.Ok(new { id = await db.CreateOrderAsync(input) }));
api.MapPost("/orders/{id:int}/receive", async (int id, StockStore db) => { await db.ReceiveAsync(id); return Results.Ok(); });
api.MapPost("/photos", async (HttpRequest request, StockStore db) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "Choose a JPEG or PNG photo." });
    var form = await request.ReadFormAsync(); var photo = form.Files.GetFile("photo");
    if (photo is null || photo.Length is < 8 or > 6 * 1024 * 1024) return Results.BadRequest(new { error = "Choose a JPEG or PNG photo smaller than 6 MB." });
    await using var source = photo.OpenReadStream(); var header = new byte[8]; await source.ReadExactlyAsync(header);
    var png = header.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10});
    var jpeg = header[0] == 255 && header[1] == 216 && header[2] == 255;
    if (!png && !jpeg) return Results.BadRequest(new { error = "Only JPEG and PNG files are accepted." });
    var dir = Path.Combine(storagePath, "photos"); Directory.CreateDirectory(dir);
    var name = Guid.NewGuid().ToString("N") + (png ? ".png" : ".jpg");
    await using (var target = File.Create(Path.Combine(dir, name))) { await target.WriteAsync(header); await source.CopyToAsync(target); }
    try { return Results.Ok(new { id = await db.SavePhotoAsync(name), status = "manual_review", message = "Photo saved. Confirm the count below. Automatic recognition requires a vision service." }); }
    catch { File.Delete(Path.Combine(dir, name)); throw; }
}).DisableAntiforgery();
api.MapGet("/photos/{id:int}", async (int id, StockStore db) =>
{
    var name = await db.PhotoAsync(id);
    return Results.File(Path.Combine(storagePath, "photos", name), name.EndsWith(".png") ? "image/png" : "image/jpeg");
});
app.Run();
