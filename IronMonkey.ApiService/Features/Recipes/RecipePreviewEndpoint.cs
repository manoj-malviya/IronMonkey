using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;
using IronMonkey.Data.RecipeContent;

namespace IronMonkey.ApiService.Features.Recipes;

public class RecipePreviewEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/recipes/{id}", Handle)
        .WithSummary("Preview full recipe content")
        .WithTags("Recipes")
        .AllowAnonymous();

    public record Response(
        Guid Id,
        string Name,
        string Description,
        RecipeContentModel Content);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var recipe = await centralDb.IndustryRecipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive, cancellationToken);

        if (recipe is null)
            return TypedResults.NotFound();

        var content = JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson) ?? new RecipeContentModel();

        return TypedResults.Ok(new Response(recipe.Id, recipe.Name, recipe.Description, content));
    }
}
