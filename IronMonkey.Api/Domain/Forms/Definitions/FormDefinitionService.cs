using System.ComponentModel.DataAnnotations;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Core;
using IronMonkey.Api.Domain.Forms.Definitions;
using IronMonkey.Api.Dtos;

namespace IronMonkey.Api.Services;

public class FormDefinitionService
{
    private readonly IFormDefinitionRepository _formRepository;

    public FormDefinitionService(IRepositoryManager repositoryManager)
    {
        _formRepository = repositoryManager.FormDefinition;
    }

    public async Task<ValidationResult> Validate(FormDefinition form)
    {
        // //get Form
        // var form = await _formRepository.Get(createRecord.formId);

        // //bool isValid = Validate(createRecord, form);

        return null;
    }

    // private List<ValidationResult> Validate(CreateRecord record, FormDefinition definition)
    // {
    //     if (record == null)
    //     {
    //         return null;
    //     };

    //     var rules = new List<FieldValidationRule>();

    //     foreach (var field in definition.Fields)
    //     {
    //         foreach (var validator in field.Validators)
    //         {
    //             rules.Add(new FieldValidationRule("Value", validator.Type, validator.Value));
    //         }
    //     }

    //     bool valid = true;
    //     List<ValidationResult> validationResults = new();
    //     foreach (var f in record.Fields)
    //     {
    //         valid &= _validatorService.ValidateModel<Field>(f, rules, validationResults);
    //     }

    //     return validationResults;
    // }

    public async Task<PagedList<FormDefinition>> GetForms(int page, int pageSize)
    {
        return await _formRepository.GetList(page, pageSize);
    }
}