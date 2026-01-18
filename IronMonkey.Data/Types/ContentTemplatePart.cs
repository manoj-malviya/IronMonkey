using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Newtonsoft.Json;

namespace IronMonkey.Data.Types;

public sealed class ContentTemplatePart
{
    [JsonConstructor]
    private ContentTemplatePart(Guid id, string name, bool isRequired, int minLength, int maxLength, int order, string description)
    {
        this.Id = id;
        this.Name = name;
        this.IsRequired = isRequired;
        this.MinLength = minLength;
        this.MaxLength = maxLength;
        this.Order = order;
        this.Description = description;
    }
    
    public Guid Id { get; init; }
    public string Name { get; init; }
    public bool IsRequired { get; init; }
    public int MinLength { get; init; }
    public int MaxLength { get; init; }
    public int Order { get; init; }
    public string Description { get; init; }
    
    public static ContentTemplatePart Create(string name, bool isRequired, int minLength, int maxLength, int order, string description)
    {
        return new ContentTemplatePart(Guid.NewGuid(), name, isRequired, minLength, maxLength, order, description);
    }
    
    // public static List<ContentTemplatePart> CreateFromJson(string json)
    // {
    //     return JsonConvert.DeserializeObject<List<ContentTemplatePart>>(json, new JsonSerializerSettings
    //     {
    //         ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor
    //     })!;
    // }
}