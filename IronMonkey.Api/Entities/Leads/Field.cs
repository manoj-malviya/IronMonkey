using IronMonkey.Api.Entities.Leads;
using IronMonkey.Api.Entities.Leads.Definitions;

namespace IronMonkey.Api.Entities.Leads;

public class Field {
    public string Name { get; set;}
    public string Value { get; set;}
    public FieldType FieldType { get; set;}
}