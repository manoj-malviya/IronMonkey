using IronMonkey.Api.Contracts;
using IronMonkey.Api.Infrastructures.MongoDb;

namespace IronMonkey.Api.Repository
{
    public class RepositoryManager : IRepositoryManager
    {
        private readonly IMongoDbContext _repositoryContext;
        private IFormDefinitionRepository? _formRepository;

        public RepositoryManager(IMongoDbContext repositoryContext)
        {
            _repositoryContext = repositoryContext;
        }

        public IFormDefinitionRepository FormDefinition
        {
            get
            {
                if (_formRepository == null)
                    _formRepository = new FormDefinitionRepository(_repositoryContext);

                return _formRepository;
            }
        }
        //public void Save() => _repositoryContext.SaveChanges();
    }
}