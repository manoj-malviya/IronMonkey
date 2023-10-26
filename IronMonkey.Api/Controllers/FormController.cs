using System;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Domain;
using IronMonkey.Api.Dtos;
using IronMonkey.Api.Entities.Models;
using IronMonkey.Api.Entities.Records;
using IronMonkey.Api.JwtFeatures;
using IronMonkey.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IronMonkey.Api.Controllers
{
    [ApiController]
    [Route("form")]
    public class FormController : ControllerBase
    {
        private readonly RecordService _recordSevice;
        public FormController(RecordService recordSevice)
        {
            _recordSevice = recordSevice;
            
        }

        [HttpPost(Name = "save")]
        public async Task<IActionResult> Save()
        {
            var record = new CreateRecord();
            record.formId = "649931372d34a37c37727dfc";
            record.CustomerName = "Manoj Malviya";
            record.CustomerMobile = "9001439744";
            record.Fields = new List<Field>() {
                new Field("Age", "10", FieldType.Integer),
                new Field("Gender", "", FieldType.String)
            };
            await _recordSevice.Validate(record);
        
            return new OkResult();
        }
    }

}
