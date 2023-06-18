using IronMonkey.Api.Contracts;
using IronMonkey.Api.Infrastructures.MongoDb;

namespace IronMonkey.Api.Repository
{
    public class RepositoryManager : IRepositoryManager
    {
        private IMongoDbContext _repositoryContext;
        // private ICompanyRepository? _companyRepository;

        public RepositoryManager(IMongoDbContext repositoryContext)
        {
            _repositoryContext = repositoryContext;
        }

        // public ICompanyRepository Company
        // {
        //     get
        //     {
        //         if (_companyRepository == null)
        //             _companyRepository = new CompanyRepository(_repositoryContext);

        //         return _companyRepository;
        //     }
        // }

        // public void Save() => _repositoryContext.SaveChanges();
    }
}