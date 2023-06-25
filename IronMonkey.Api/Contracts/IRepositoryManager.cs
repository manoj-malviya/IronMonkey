using IronMonkey.Api.Repository;

namespace IronMonkey.Api.Contracts
{
    public interface IRepositoryManager
    {
        IFormRepository Form { get; }
        //void Save();
    }
}