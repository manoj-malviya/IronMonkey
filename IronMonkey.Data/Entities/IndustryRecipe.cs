using IronMonkey.Data.Abstractions;
using IronMonkey.Data.RecipeContent;

namespace IronMonkey.Data.Entities;

public sealed class IndustryRecipe : Entity
{
    private IndustryRecipe(Guid id, string name, string description, string industrySlug,
        string iconIdentifier, bool isBlank, string contentJson)
        : base(id)
    {
        Name = name;
        Description = description;
        IndustrySlug = industrySlug;
        IconIdentifier = iconIdentifier;
        IsBlank = isBlank;
        ContentJson = contentJson;
        Version = 1;
        IsActive = true;
    }

    private IndustryRecipe() { }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string IndustrySlug { get; private set; } = string.Empty;
    public string IconIdentifier { get; private set; } = string.Empty;
    public bool IsBlank { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Version { get; private set; } = 1;
    public string ContentJson { get; private set; } = "{}";

    public static IndustryRecipe Create(string name, string description, string industrySlug,
        string iconIdentifier, bool isBlank, RecipeContentModel content)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(content);
        return new IndustryRecipe(Guid.NewGuid(), name, description, industrySlug, iconIdentifier, isBlank, json);
    }

    public void UpdateContent(RecipeContentModel content)
    {
        ContentJson = System.Text.Json.JsonSerializer.Serialize(content);
        Version++;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
