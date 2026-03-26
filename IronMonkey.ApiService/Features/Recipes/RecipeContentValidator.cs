using FluentValidation;
using IronMonkey.Data.RecipeContent;

namespace IronMonkey.ApiService.Features.Recipes;

public class RecipeContentValidator : AbstractValidator<RecipeContentModel>
{
    private static readonly string[] ValidStageTypes = ["Entry", "Active", "ClosedWon", "ClosedLost"];
    private static readonly string[] ValidFieldTypes = ["Text", "Number", "Date", "Dropdown", "MultiSelect", "Currency", "Boolean"];

    public RecipeContentValidator()
    {
        RuleFor(x => x.PipelineStages)
            .NotEmpty().WithMessage("Recipe must have at least one pipeline stage.");

        RuleForEach(x => x.PipelineStages).ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty().WithMessage("Stage name is required.");
            s.RuleFor(x => x.StageType)
                .Must(st => ValidStageTypes.Contains(st))
                .WithMessage($"StageType must be one of: {string.Join(", ", ValidStageTypes)}.");
        });

        RuleForEach(x => x.CustomFields).ChildRules(f =>
        {
            f.RuleFor(x => x.FieldName).NotEmpty().WithMessage("Field name is required.");
            f.RuleFor(x => x.FieldType)
                .Must(ft => ValidFieldTypes.Contains(ft))
                .WithMessage($"FieldType must be one of: {string.Join(", ", ValidFieldTypes)}.");
        });

        RuleForEach(x => x.WorkflowRules).ChildRules(r =>
        {
            r.RuleFor(x => x.Name).NotEmpty().WithMessage("Workflow rule name is required.");
        });
    }
}
