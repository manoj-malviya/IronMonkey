using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Dtos;
using IronMonkey.Api.Entities.Leads.Definitions;
using IronMonkey.Api.Entities.Models;
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
    [Route("record")]
    public class RecordController : ControllerBase
    {
        private readonly IRecordRepository _recordRepo;
        public RecordController(IRepositoryManager repositoryManager)
        {
            _recordRepo = repositoryManager.Record;
        }

        [HttpPost()]
        public async Task<IActionResult> Create()
        {
            var v = new Record(){
                Name = "Enquiry",
                Fields = new List<FieldDefinition>(){
                    new FieldDefinition("Age", FieldType.Integer),
                    new FieldDefinition("City", FieldType.String)
                }
            };
            _recordRepo.Create(v);

            return new OkResult();
        }
    }

}
