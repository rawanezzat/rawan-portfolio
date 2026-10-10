using Credit2.Seeds;
using Microsoft.AspNetCore.Identity;
using System.Data;

namespace Credit2.Seeds
{
    public static class DefaultRoles
    {
        // FIXED: Changed IdentityRole to IdentityRole<int>
        public static async Task SeedAsync(RoleManager<IdentityRole<int>> roleManager)
        {
            if (!roleManager.Roles.Any())
            {
                await roleManager.CreateAsync(new IdentityRole<int>(AppRoles.Admin));
                await roleManager.CreateAsync(new IdentityRole<int>(AppRoles.Accountant));
                await roleManager.CreateAsync(new IdentityRole<int>(AppRoles.Holder));
                await roleManager.CreateAsync(new IdentityRole<int>(AppRoles.Auditor));
                await roleManager.CreateAsync(new IdentityRole<int>(AppRoles.BoardMember));
            }
        }
    }
}