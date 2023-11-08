using IronMonkey.Api.Contracts;
using IronMonkey.Api.Data.MongoDb;

namespace IronMonkey.Api.Repository
{
    public class RepositoryManager : IRepositoryManager
    {
        private readonly MongoDbContext _repositoryContext;
        private IFormDefinitionRepository? _formRepository;

        public RepositoryManager(MongoDbContext repositoryContext)
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