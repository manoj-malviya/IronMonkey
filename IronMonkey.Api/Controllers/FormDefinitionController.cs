using IronMonkey.Api.Contracts;
using IronMonkey.Api.Controllers.Requests;
using IronMonkey.Api.Domain.Forms.Definitions;
using IronMonkey.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace IronMonkey.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class FormDefinitionController : ControllerBase
    {
        private readonly IFormDefinitionRepository _formRepo;
        private readonly FormDefinitionService _service;
        public FormDefinitionController(IRepositoryManager repositoryManager, FormDefinitionService formDefinitionService)
        {
            _formRepo = repositoryManager.FormDefinition;
            _service = formDefinitionService;
        }

        [HttpPost]
        public IActionResult Create([FromBody] CreateFormDefinitionRequest form)
        {
            
            var v = new FormDefinition() {
                Name = form.Name,
                Collection = form.Storage,
                Fields = form.Fields
            };

            _formRepo.Create(v);

            return new OkResult();
        }

        [HttpGet]
        public IActionResult GetList()
        {
            return Ok(_service.GetForms(1, 5));
        }
    }

}
