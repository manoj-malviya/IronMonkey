using System.ComponentModel.DataAnnotations;
using IronMonkey.Api.Infrastructures.Validations.Rules;
using Newtonsoft.Json;

namespace IronMonkey.Api.Infrastructures.Validations;

public class ValidatorService
{
    public bool ValidateModel<T>(T model, string validationRulesJson)
    {
        var validationRules = JsonConvert.DeserializeObject<List<ValidationRule>>(validationRulesJson);

        var validationResults = new List<ValidationResult>();
        foreach (var rule in validationRules)
        {
            if (!ValidateProperty(model, rule, validationResults))
            {
                // Handle validation failure
                var errorMessages = validationResults.Select(r => r.ErrorMessage);
                Console.WriteLine(string.Join(Environment.NewLine, errorMessages));
                return false;
            }
        }

        return true;
    }

    private bool ValidateProperty<T>(T model, ValidationRule rule, List<ValidationResult> validationResults)
    {
        var validationContext = new ValidationContext(model);
        validationContext.MemberName = rule.Type; // Use rule.Type as the property name for validation

        var validationAttribute = GetValidationAttribute(rule.Type, rule.Value);
        var isValid = Validator.TryValidateProperty(model.GetType().GetProperty(rule.Type).GetValue(model), validationContext, validationResults);

        if (!isValid)
        {
            // Add custom error message to the validation results
            var validationError = new ValidationResult(rule.Message, new[] { rule.Type });
            validationResults.Add(validationError);
        }

        return isValid;
    }

    private ValidationAttribute GetValidationAttribute(string ruleType, object ruleValue)
    {
        switch (ruleType.ToLower())
        {
            case "required":
                return new RequiredAttribute();
            case "minlength":
                if (ruleValue is int minLength)
                    return new MinLengthAttribute(minLength);
                break;
            case "maxlength":
                if (ruleValue is int maxLength)
                    return new MaxLengthAttribute(maxLength);
                break;
            // Add more cases for other validation types as needed
        }

        // Default to a validation attribute that always passes if no matching attribute is found
        return null;
    }
}