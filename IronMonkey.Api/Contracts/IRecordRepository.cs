using IronMonkey.Api.Entities.Leads.Definitions;

namespace IronMonkey.Api.Contracts;

public interface IFormRepository {
    public void Create(Forn record);
}