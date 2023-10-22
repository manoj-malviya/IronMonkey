using IronMonkey.Api.Entities.Records;

namespace IronMonkey.Api.Dtos;

public class CreateRecord {
    public string formId { get; set;}
    public string CustomerName { get; set;}
    public string CustomerMobile {get; set;}
    public IEnumerable<Field> Fields { get; set;} 
}