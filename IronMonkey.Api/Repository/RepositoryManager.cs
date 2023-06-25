using IronMonkey.Api.Contracts;
using IronMonkey.Api.Infrastructures.MongoDb;

namespace IronMonkey.Api.Repository
{
    public class RepositoryManager : IRepositoryManager
    {
        private readonly IMongoDbContext _repositoryContext;
        private IFormRepository? _formRepository;

        public RepositoryManager(IMongoDbContext repositoryContext)
        {
            _repositoryContext = repositoryContext;
        }

        public IRecordRepository Form
        {
            get
            {
                if (_formRepository == null)
                    _formRepository = new FormRepository(_repositoryContext);

                return _formRepository;
            }
        }
        //public void Save() => _repositoryContext.SaveChanges();
    }
}