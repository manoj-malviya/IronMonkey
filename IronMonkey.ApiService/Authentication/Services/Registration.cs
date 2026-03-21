using Microsoft.AspNetCore.Identity;

namespace IronMonkey.ApiService.Authentication.Services;

public class Registration(RoleManager<IdentityRole> roleManager)
{
    public async void RegisterAsContractor()
    {
        var roleExist = await roleManager.RoleExistsAsync("Admin");
        if (!roleExist)
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole("Admin"));
        }
    }
}