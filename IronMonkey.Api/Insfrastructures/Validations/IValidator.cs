using System.ComponentModel.DataAnnotations;

namespace IronMonkey.Api.Infrastructures.Validations;

public interface IValidator {

    public bool IsValid(ValidationRule rule, ValidationContext context); 
}