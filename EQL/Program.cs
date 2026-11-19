using EQL.Data;
using EQL.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Database: SQLite file for testing now. To move to MySQL later, set "DatabaseProvider": "MySql"
// in appsettings.json, add the MySQL connection string, and uncomment the UseMySql line
// (needs the Pomelo.EntityFrameworkCore.MySql 9.0.x package).
var provider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Data Source=App_Data/eql.db";
// Make the SQLite file path relative to the project folder, not whatever folder the app starts in.
connectionString = connectionString.Replace("Data Source=App_Data",
    "Data Source=" + Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Services.AddDbContext<EqlDbContext>(options =>
{
    if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
    {
        // options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        throw new InvalidOperationException("MySQL isn't set up yet. See the comment in Program.cs.");
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});
builder.Services.AddScoped<ItemCsvImporter>();

var app = builder.Build();

// Create the database and tables on first run (fine for a test database; switch to
// EF Core migrations once the schema settles).
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<EqlDbContext>().Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
