using System.ComponentModel.DataAnnotations;
using IronMonkey.Api.Domain.Forms.Definitions;
using IronMonkey.Api.Infrastructures.Validations.Rules;

namespace IronMonkey.Api.Infrastructures.Validations;

public class ValidatorService
{
    public bool ValidateModel<T>(T model, IEnumerable<FieldValidationRule> validationRules, List<ValidationResult> validationResults)
    {
        foreach (var rule in validationRules)
        {
            if (!ValidateProperty(model, rule, validationResults))
            {
                // Handle validation failure
                var errorMessages = validationResults.Select(r => r.ErrorMessage);
                return false;
            }
        }

        return true;
    }

    private bool ValidateProperty<T>(T model, FieldValidationRule rule, List<ValidationResult> validationResults)
    {
        var validationContext = new ValidationContext(model);
        validationContext.MemberName = rule.Type; // Use rule.Type as the property name for validation

        var validator = GetValidationAttribute(rule.Type, rule.Value);

        //validationAttribute.Validate(model.GetType().GetProperty(rule.Property).GetValue(model), validationContext);  // Validator.TryValidateProperty(model.GetType().GetProperty(rule.Property).GetValue(model), validationContext, validationResults);
        var result = validator.IsValid(rule, validationContext);
        if (!result)
        {
            // Add custom error message to the validation results
            var validationError = new ValidationResult(rule.Message, new[] { rule.Property });
            validationResults.Add(validationError);
        }

        return result;
    }

    private IValidator GetValidationAttribute(string ruleType, object ruleValue)
    {
        switch (ruleType.ToLower())
        {
            case "required":
                return new RequiredValidator();
            // case "minlength":
            //     if (ruleValue is int minLength)
            //         return new MinLengthAttribute(minLength);
            //     break;
            // case "maxlength":
            //     if (ruleValue is int maxLength)
            //         return new MaxLengthAttribute(maxLength);
            //     break;
            // Add more cases for other validation types as needed
        }

        // Default to a validation attribute that always passes if no matching attribute is found
        return null;
    }
}