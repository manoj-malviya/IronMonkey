using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IronMonkey.Data;

public static class Seed
{
    // public static async Task Generate(this IServiceProvider serviceProvider)
    // {
    //     var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    //     string[] roles = new string[] { "Admin", "Contractor", "Writer" };
    //     foreach (var role in roles)
    //     {
    //         var roleExist = await roleManager!.RoleExistsAsync(role);
    //         if (!roleExist)
    //         {
    //             var roleResult = await roleManager!.CreateAsync(new IdentityRole("Admin"));
    //         }
    //     }
    //     
    // }
}