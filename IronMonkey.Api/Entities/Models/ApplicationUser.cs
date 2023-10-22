using AspNetCore.Identity.Mongo.Model;
using Microsoft.AspNetCore.Identity;

namespace IronMonkey.Api.Entities.Models
{
    public class ApplicationUser : MongoUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    public class ApplicationRole: MongoRole
    {
        public ApplicationRole(string role) : base(role)
        {
            // Additional initialization or logic if needed
        }
    }

}
