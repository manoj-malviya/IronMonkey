namespace IronMonkey.Api.Infrastructures.Validations.Rules;

using IronMonkey.Api.Infrastructures.Validations;
using System.ComponentModel.DataAnnotations;

public class RequiredValidator : IValidator
{
    public RequiredValidator()
    {
        ErrorMessage = "This field is required.";
    }

    public string ErrorMessage { get; set; }

    public bool IsValid(ValidationRule rule, ValidationContext validationContext)
    {
        //Get PropertyInfo Object  
        var basePropertyInfo = validationContext.ObjectType.GetProperty(rule.Property);

        //Get Value of the property  
        var value = basePropertyInfo.GetValue(validationContext.ObjectInstance, null);
        if (value == null)
        {
            return false;
        }
        
        if (value is string stringValue && string.IsNullOrWhiteSpace(stringValue))
        {
            return false;
        }

        return true;
    }
}