using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;
using IronMonkey.Data.RecipeContent;

namespace IronMonkey.ApiService.Features.Recipes;

public class RecipeListEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/recipes", Handle)
        .WithSummary("List available recipes")
        .WithTags("Recipes")
        .AllowAnonymous();

    public record Response(
        Guid Id,
        string Name,
        string Description,
        string IconIdentifier,
        string IndustrySlug,
        bool IsBlank,
        int Version,
        int StageCount,
        int FieldCount,
        int RuleCount,
        int RoleCount);

    private static async Task<Ok<List<Response>>> Handle(
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var recipes = await centralDb.IndustryRecipes
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.IsBlank)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var responses = recipes.Select(r =>
        {
            var content = JsonSerializer.Deserialize<RecipeContentModel>(r.ContentJson) ?? new RecipeContentModel();
            return new Response(
                r.Id,
                r.Name,
                r.Description,
                r.IconIdentifier,
                r.IndustrySlug,
                r.IsBlank,
                r.Version,
                content.PipelineStages.Count,
                content.CustomFields.Count,
                content.WorkflowRules.Count,
                content.Roles.Count);
        }).ToList();

        return TypedResults.Ok(responses);
    }
}
