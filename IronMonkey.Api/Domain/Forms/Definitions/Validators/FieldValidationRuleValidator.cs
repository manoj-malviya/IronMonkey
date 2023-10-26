using FluentValidation;
using IronMonkey.Api.Domain.Forms.Definitions;

public class FieldValidationRuleValidator : AbstractValidator<FieldValidationRule> 
{
    public FieldValidationRuleValidator()
    {
        RuleFor(fd => fd.Type).NotNull();
        RuleFor(fd => fd.Value).NotNull();
    }
}