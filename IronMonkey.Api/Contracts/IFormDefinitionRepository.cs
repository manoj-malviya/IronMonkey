using IronMonkey.Api.Entities.Forms.Definitions;

namespace IronMonkey.Api.Contracts;

public interface IFormDefinitionRepository {
    public void Create(FormDefinition form);

    public Task<FormDefinition> Get(string Id);
}