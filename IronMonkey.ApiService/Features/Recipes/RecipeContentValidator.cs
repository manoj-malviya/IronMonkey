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

    // Mirror ChargeType, BillingFrequency and CustomFieldEntity by name, for the same reason.
    private static readonly string[] ValidChargeTypes = ["OneOff", "Recurring"];
    private static readonly string[] ValidFrequencies = ["None", "Monthly", "Quarterly", "PerTerm", "Annually"];
    private static readonly string[] ValidFieldScopes = ["Lead", "Contact", "Product"];

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

        RuleForEach(x => x.CustomFields).ChildRules(f =>
        {
            f.RuleFor(x => x.AppliesTo)
                .Must(a => a is null || ValidFieldScopes.Contains(a, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"AppliesTo must be one of: {string.Join(", ", ValidFieldScopes)}.");
        });

        // Catalog, validated only when present — a null section is a legacy recipe.
        RuleForEach(x => x.Catalog!.Products).ChildRules(p =>
        {
            p.RuleFor(x => x.Code).NotEmpty().MaximumLength(64).WithMessage("Product code is required (64 characters max).");
            p.RuleFor(x => x.Name).NotEmpty().WithMessage("Product name is required.");
            p.RuleFor(x => x.ChargeType)
                .Must(c => ValidChargeTypes.Contains(c, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"ChargeType must be one of: {string.Join(", ", ValidChargeTypes)}.");
            p.RuleFor(x => x.BillingFrequency)
                .Must(f => ValidFrequencies.Contains(f, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"BillingFrequency must be one of: {string.Join(", ", ValidFrequencies)}.");
            p.RuleFor(x => x.DefaultPeriods).InclusiveBetween(1, 1200);
            p.RuleFor(x => x.DefaultTaxRatePercent).InclusiveBetween(0, 100);
            p.RuleFor(x => x.ListPrice).GreaterThanOrEqualTo(0).When(x => x.ListPrice is not null);
        }).When(x => x.Catalog is not null);

        // Codes are unique per tenant case-insensitively; a recipe with a duplicate would
        // fail mid-provisioning on the tenant's unique index.
        RuleFor(x => x.Catalog)
            .Must(c => c!.Products.GroupBy(p => p.Code.Trim().ToLowerInvariant()).All(g => g.Count() == 1))
            .WithMessage("Two catalog products cannot share a code.")
            .When(x => x.Catalog is not null);

        RuleForEach(x => x.WorkflowRules).ChildRules(r =>
        {
            r.RuleFor(x => x.Name).NotEmpty().WithMessage("Workflow rule name is required.");
        });
    }
}
