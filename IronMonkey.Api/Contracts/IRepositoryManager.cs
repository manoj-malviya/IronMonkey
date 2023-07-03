using IronMonkey.Api.Repository;

namespace IronMonkey.Api.Contracts
{
    public interface IRepositoryManager
    {
        IFormDefinitionRepository FormDefinition { get; }
        
        //void Save();
    }
}