namespace IronMonkey.Api.Infrastructures.Validations.Rules;

using System.ComponentModel.DataAnnotations;

public class RequiredValidator : ValidationAttribute
{
    public RequiredValidator()
    {
        ErrorMessage = "This field is required.";
    }

    protected ValidationResult IaValid(ValidationRule rule, ValidationContext validationContext)
    {
        //Get PropertyInfo Object  
        var basePropertyInfo = validationContext.ObjectType.GetProperty(rule.Property);

        //Get Value of the property  
        var value = basePropertyInfo.GetValue(validationContext.ObjectInstance, null);
        if (value == null)
        {
            return new ValidationResult(ErrorMessage);
        }
        
        if (value is string stringValue && string.IsNullOrWhiteSpace(stringValue))
        {
            return new ValidationResult(ErrorMessage);
        }

        return ValidationResult.Success;
    }
}