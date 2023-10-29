using System.ComponentModel.DataAnnotations;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Domain.Forms.Definitions;
using IronMonkey.Api.Dtos;
using IronMonkey.Api.Infrastructures.Validations;

namespace IronMonkey.Api.Domain.Records;

public class RecordService {
    private readonly IFormDefinitionRepository _formRepository;
    // private readonly RecordRepository _recordRepository;
    private readonly ValidatorService _validatorService;

    public RecordService(IRepositoryManager repositoryManager, ValidatorService validatorService) {
        _formRepository = repositoryManager.FormDefinition;
        // _recordRepository = repositoryManager;
        _validatorService = validatorService;
        
    }

    public async Task<ValidationResult> Validate(CreateRecord createRecord) {
        //get Form
        var form = await _formRepository.Get(createRecord.formId);
        
        //bool isValid = Validate(createRecord, form);

        return null;
    }

    private List<ValidationResult> Validate(CreateRecord record, FormDefinition definition)
    {
        if (record == null) {
            return null;
        };

        var rules = new List<FieldValidationRule>();

        foreach (var field in definition.Fields)
        {
            foreach (var validator in field.Validators)
            {
                rules.Add(new FieldValidationRule("Value", validator.Type, validator.Value));
            }
        }

        bool valid = true;
        List<ValidationResult> validationResults = new();
        foreach(var f in record.Fields) {
            valid &= _validatorService.ValidateModel<Field>(f, rules, validationResults);
        }

        return validationResults;
    }    
}