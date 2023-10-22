using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using IronMonkey.Api.Dtos;
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
    [Route("account")]
    public class AccountController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        public AccountController(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [HttpPost()]
        public async Task<IActionResult> Create([FromBody] CreateUser payload)
        {
            var user = new ApplicationUser { Email = payload.Email, UserName = payload.Email };
            await _userManager.CreateAsync(user);

            //prepare and send an email for the email confirmation

            await _userManager.AddToRoleAsync(user, "Member");

            return new OkResult();
        }

        [HttpPost("role")]
        public async Task<IActionResult> CreateRole()
        {
            string[] roleNames = { "Admin", "Manager", "Member" };
            IdentityResult roleResult;

            foreach (var roleName in roleNames)
            {
                var roleExist = await _roleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    //create the roles and seed them to the database: Question 1
                    roleResult = await _roleManager.CreateAsync(new ApplicationRole(roleName));
                }
            }

            return new OkResult();
        }
    }

}
