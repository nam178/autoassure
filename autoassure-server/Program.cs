using System.Text;
using A2.Server.Common;
using A2.Server.Repositories;
using A2.Server.Services;
using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var ssmParameterPath = builder.Configuration["AUTOASSURE_SSM_PARAMETER_PATH"];
if (!DesignTimeBuild.IsActive && !string.IsNullOrEmpty(ssmParameterPath))
{
    builder.Configuration.AddSystemsManager(ssmParameterPath);
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection("OAuth:Google"));
builder.Services.Configure<AuthTokenOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<DynamoDbOptions>(builder.Configuration.GetSection("DynamoDb"));
builder.Services.AddHttpClient<IGoogleTokenExchangeService, GoogleTokenExchangeService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IRefreshTokenRepository, DynamoDbRefreshTokenRepository>();
builder.Services.AddScoped<IAuthTokenService, AuthTokenService>();
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddHostedService<ConfigValidationHostedService>();

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwtOptions =>
    {
        var tokenOptions = builder.Configuration.GetSection("Auth").Get<AuthTokenOptions>()!;
        jwtOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = tokenOptions.Issuer,
            ValidAudience = tokenOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(tokenOptions.SigningKey)
            ),
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public abstract partial class Program { }
