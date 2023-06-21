using IronMonkey.Api.Contracts;
using IronMonkey.Api.Infrastructures.MongoDb;

namespace IronMonkey.Api.Repository
{
    public class RepositoryManager : IRepositoryManager
    {
        private readonly IMongoDbContext _repositoryContext;
        private RecordRepository? _recordRepository;

        public RepositoryManager(IMongoDbContext repositoryContext)
        {
            _repositoryContext = repositoryContext;
        }

        public IRecordRepository Record
        {
            get
            {
                if (_recordRepository == null)
                    _recordRepository = new RecordRepository(_repositoryContext);

                return _recordRepository;
            }
        }
        //public void Save() => _repositoryContext.SaveChanges();
    }
}