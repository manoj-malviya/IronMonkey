using FluentValidation;
using IronMonkey.Api.Domain.Forms.Definitions;

public class FieldDefinitionValidator : AbstractValidator<FieldDefinition> 
{
    public FieldDefinitionValidator()
    {
        RuleFor(fd => fd.Name).NotNull();
        RuleFor(fd => fd.FieldType).IsInEnum();
        RuleForEach(fd => fd.Validators).SetValidator(new FieldValidationRuleValidator());
    }
}