using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Recipes;

public class DeactivateRecipeEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/recipes/{id}", Handle)
        .WithSummary("Deactivate a recipe")
        .WithTags("Platform Admin")
        .RequireAuthorization();

    public record Response(Guid RecipeId, string Message);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var recipe = await centralDb.IndustryRecipes
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive, cancellationToken);

        if (recipe is null)
            return TypedResults.NotFound();

        recipe.Deactivate();
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(recipe.Id, "Recipe deactivated successfully."));
    }
}
