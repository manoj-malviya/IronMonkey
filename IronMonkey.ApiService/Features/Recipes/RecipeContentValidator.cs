using FluentValidation;
using IronMonkey.Data.RecipeContent;

namespace IronMonkey.ApiService.Features.Recipes;

public class RecipeContentValidator : AbstractValidator<RecipeContentModel>
{
    private static readonly string[] ValidStageTypes = ["Entry", "Active", "ClosedWon", "ClosedLost"];
    private static readonly string[] ValidFieldTypes = ["Text", "Number", "Date", "Dropdown", "MultiSelect", "Currency", "Boolean"];

    /// <summary>Mirrors <c>PipelineRecordType</c> by name; Recipes cannot reference Data's
    /// entities, so the value travels as a string exactly as StageType already does.</summary>
    private static readonly string[] ValidRecordTypes = ["Lead", "Opportunity"];

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

        // Validated only when present. A null section is a legacy recipe with no opinion on
        // opportunity stages, which provisioning handles by seeding the defaults — requiring
        // the section here would reject every recipe stored before it existed.
        RuleForEach(x => x.OpportunityStages!).ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty().WithMessage("Opportunity stage name is required.");
            s.RuleFor(x => x.StageType)
                .Must(st => ValidStageTypes.Contains(st))
                .WithMessage($"StageType must be one of: {string.Join(", ", ValidStageTypes)}.");
        }).When(x => x.OpportunityStages is not null);

        // Named pipelines, validated only when present — same precedent again, and for the
        // same reason: a null section is a legacy recipe with no opinion on pipelines, and
        // requiring it would reject every recipe in the catalog.
        //
        // Note what is NOT required here: a pipeline's Stages may be empty. Provisioning
        // seeds the product defaults for that case, because a pipeline with no stages accepts
        // no records and is never what an author meant.
        RuleForEach(x => x.Pipelines!).ChildRules(p =>
        {
            p.RuleFor(x => x.Name).NotEmpty().WithMessage("Pipeline name is required.");

            // Case-insensitive, matching how provisioning parses it. A validator stricter
            // than the consumer rejects recipes that would in fact have provisioned fine.
            p.RuleFor(x => x.RecordType)
                .Must(rt => ValidRecordTypes.Contains(rt, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Pipeline RecordType must be one of: {string.Join(", ", ValidRecordTypes)}.");

            p.RuleForEach(x => x.Stages).ChildRules(s =>
            {
                s.RuleFor(x => x.Name).NotEmpty().WithMessage("Pipeline stage name is required.");
                s.RuleFor(x => x.StageType)
                    .Must(st => ValidStageTypes.Contains(st))
                    .WithMessage($"StageType must be one of: {string.Join(", ", ValidStageTypes)}.");
            });
        }).When(x => x.Pipelines is not null);

        // Two pipelines a user cannot tell apart in a picker are a configuration mistake, and
        // the tenant database's unique index would reject the second one mid-provisioning —
        // far better to refuse the recipe at authoring time.
        RuleFor(x => x.Pipelines)
            .Must(pipelines => pipelines!
                .GroupBy(p => (p.RecordType?.ToLowerInvariant(), p.Name?.Trim().ToLowerInvariant()))
                .All(g => g.Count() == 1))
            .WithMessage("Two pipelines of the same record type cannot share a name.")
            .When(x => x.Pipelines is not null);

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
