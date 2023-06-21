using IronMonkey.Api.Repository;

namespace IronMonkey.Api.Contracts
{
    public interface IRepositoryManager
    {
        IRecordRepository Record { get; }
        //void Save();
    }
}