using IronMonkey.Api.Entities.Leads.Definitions;

namespace IronMonkey.Api.Contracts;

public interface IRecordRepository {
    public void Create(Record record);
}