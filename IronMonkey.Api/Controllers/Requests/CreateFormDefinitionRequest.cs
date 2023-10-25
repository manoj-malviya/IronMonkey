using System.ComponentModel.DataAnnotations;
using IronMonkey.Api.Entities.Forms.Definitions;

namespace IronMonkey.Api.Controllers.Requests;

public class CreateFormDefinitionRequest {

    [Required]
    public string Name {get; set;} = "";

    [Required]
    public string Storage {get; set;} = "";

    [Required]
    public List<FieldDefinition> Fields {get;set;} = new();
}