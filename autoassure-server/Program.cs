using A2.Server.Common;
using A2.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection("Google"));
builder.Services.Configure<TokenIssuerServiceOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddHttpClient<IGoogleTokenExchangeService, GoogleTokenExchangeService>();
builder.Services.AddScoped<ITokenIssuerService, TokenIssuerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

public partial class Program { }
