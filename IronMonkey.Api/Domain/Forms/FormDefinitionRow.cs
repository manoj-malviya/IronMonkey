using MongoDB.Bson;

namespace IronMonkey.Api.Domain.Forms;

public class FormDefinitionRow
{
    public ObjectId Id { get; set; }
    public string Name { get; set; } = String.Empty;
}