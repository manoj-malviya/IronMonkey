using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Users.Endpoints;

public class Forecast : IEndpoint
{
    private static string[] summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/weatherforecast", Handle)
            .WithName("GetWeatherForecast")
            .RequireAuthorization("users:read");
    }
    
    record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
    {
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    }

    private static Results<Ok<WeatherForecast[]>, ValidationError> Handle(AppDbContext database,
        CancellationToken cancellationToken)
    {
            var forecast = Enumerable.Range(1, 5).Select(index =>
                    new WeatherForecast
                    (
                        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                        Random.Shared.Next(-20, 55),
                        summaries[Random.Shared.Next(summaries.Length)]
                    ))
                .ToArray();
            return TypedResults.Ok(forecast);
    }
}