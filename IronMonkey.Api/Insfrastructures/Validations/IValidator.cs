using System.ComponentModel.DataAnnotations;
using IronMonkey.Api.Domain.Forms.Definitions;

namespace IronMonkey.Api.Infrastructures.Validations;

public interface IValidator {

    public bool IsValid(FieldValidationRule rule, ValidationContext context); 
}