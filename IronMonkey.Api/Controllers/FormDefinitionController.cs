using System;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Dtos;
using IronMonkey.Api.Entities.Forms;
using IronMonkey.Api.Entities.Forms.Definitions;
using IronMonkey.Api.Entities.Models;
using IronMonkey.Api.Infrastructures.Validations;
using IronMonkey.Api.Infrastructures.Validations.Rules;
using IronMonkey.Api.JwtFeatures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IronMonkey.Api.Controllers
{
    [ApiController]
    [Route("form-definition")]
    public class FormDefinitionController : ControllerBase
    {
        private readonly IFormDefinitionRepository _formRepo;
        public FormDefinitionController(IRepositoryManager repositoryManager)
        {
            _formRepo = repositoryManager.FormDefinition;
            
        }

        [HttpPost()]
        public async Task<IActionResult> Create()
        {
            var v = new FormDefinition() {
                Name = "Enquery",
                Collection = "enquery",
                Fields = new List<FieldDefinition>(){
                    new FieldDefinition("Age", FieldType.Integer, new ValidationRule[]{
                        new ValidationRule("Age", ValidationRuleType.Required, "")
                    }),
                    new FieldDefinition("Gender", FieldType.Integer, new ValidationRule[]{
                        new ValidationRule("Gender", ValidationRuleType.Required, "")
                    })
                }
            };
            _formRepo.Create(v);

            return new OkResult();
        }
    }

}
