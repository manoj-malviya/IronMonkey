using MongoDB.Bson;

public class FormDefinitionEntity
{
    public ObjectId Id {get; set;}
    public string Name {get; set;}
    public string Storage {get; set;}
}

public class FormDefinitionFieldEntity
{
    public ObjectId Id {get;set;}
    public ObjectId DefinitionId {get; set;}
    public string Name {get; set;}
    public string Type {get; set;}
}