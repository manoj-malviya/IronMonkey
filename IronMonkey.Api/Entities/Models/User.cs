using Microsoft.AspNetCore.Identity;

namespace IronMonkey.Api.Entities.Models
{
    public class User : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}