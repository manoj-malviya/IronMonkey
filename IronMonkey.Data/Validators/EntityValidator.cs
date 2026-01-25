using FluentValidation;
using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Validators;

public class EntityValidator : AbstractValidator<Entity>
{
    public EntityValidator()
    {
        RuleFor(entity => entity.OrganizationId)
            .NotEmpty().WithMessage("OrganizationId is required.");

        RuleFor(entity => entity.Id)
            .NotEmpty().WithMessage("Id is required.");
    }
}