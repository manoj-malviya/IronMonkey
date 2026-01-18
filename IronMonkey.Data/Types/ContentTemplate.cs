using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Types;

public sealed class ContentTemplate : Entity
{
    private ContentTemplate(Guid id, string name, Guid contractorId)
    {
        Id = id;
        Name = name;
        ContractorId = contractorId;
    }
    
    public string Name { get; private set; }
    public ICollection<ContentTemplatePart> Parts { get; private set; } = new List<ContentTemplatePart>();
    
    public Guid ContractorId { get; private set; }
    
    public static ContentTemplate Create(string name, Guid contractorId)
    {
        return new ContentTemplate(Guid.NewGuid(), name, contractorId);
    }
    
    public void AddPart(ContentTemplatePart part)
    {
        Parts.Add(part);
    }
    
    public void UpdatePars(ICollection<ContentTemplatePart> parts)
    {
        Parts = parts;
    }
    
    public void UpdateName(string name)
    {
        Name = name;
    }
}