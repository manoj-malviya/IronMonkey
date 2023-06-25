using System.ComponentModel.DataAnnotations;
using IronMonkey.Api.Infrastructures.Validations.Rules;
using Newtonsoft.Json;

namespace IronMonkey.Api.Infrastructures.Validations;

public class ValidatorService
{
    public bool ValidateModel<T>(T model, string validationRulesJson)
    {
        var validationRules = JsonConvert.DeserializeObject<List<ValidationRule>>(validationRulesJson);
        var validationContext = new ValidationContext(model);
        var validationResults = new List<ValidationResult>();

        foreach (var rule in validationRules)
        {
            var validator = GetValidator(rule.Type, rule.Value);
            var result = validator.Validate(validationContext);

            if (!result.IsValid)
            {
                // Handle validation failure
                // You can collect the validation errors or perform custom logic
                return false;
            }
        }

        return true;
    }

    private IValidator GetValidator(string ruleType, object ruleValue)
    {
        switch (ruleType.ToLower())
    {
        case "required":
            return new RequiredValidator();
        // case "minlength":
        //     if (ruleValue is int minLength)
        //         return new MinLengthValidator(minLength);
        //     break;
        // case "maxlength":
        //     if (ruleValue is int maxLength)
        //         return new MaxLengthValidator(maxLength);
        //     break;
        // Add more cases for other validation types as needed
    }

    // Default to a validator that always returns true if no matching validator is found
    return new NullValidator();
    }
}