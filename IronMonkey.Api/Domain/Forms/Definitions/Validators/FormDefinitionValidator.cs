using FluentValidation;
using IronMonkey.Api.Domain.Forms.Definitions;

public class FormDefinitionValidator : AbstractValidator<FormDefinition> 
{
    public FormDefinitionValidator()
    {
        RuleFor(fd => fd.Name).NotNull();
        RuleFor(fd => fd.Storage).NotNull();
        // RuleForEach(fd => fd.Fields).SetValidator(new FieldDefinitionValidator());
    }
}