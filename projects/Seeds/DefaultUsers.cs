// Seeds/DefaultUsers.cs
using Credit2.Core.Models;
using Microsoft.AspNetCore.Identity;

namespace Credit2.Seeds
{
    public static class DefaultUsers
    {
        public static async Task SeedAdminUserAsync(UserManager<User> userManager)
        {
            User admin = new()
            {
                UserName = "admin@credit2.com",
                Email = "admin@credit2.com",
                Name = "System Administrator",
                Role = AppRoles.Admin,
                EmailConfirmed = true,
                CreatedAt = DateTime.Now
            };

            var user = await userManager.FindByEmailAsync(admin.Email);

            if (user is null)
            {
                await userManager.CreateAsync(admin, "P@ssword123");
                await userManager.AddToRoleAsync(admin, AppRoles.Admin);
            }
        }

        public static async Task SeedAccountantUserAsync(UserManager<User> userManager)
        {
            User accountant = new()
            {
                UserName = "accountant@credit2.com",
                Email = "accountant@credit2.com",
                Name = "Default Accountant",
                Role = AppRoles.Accountant,
                EmailConfirmed = true,
                CreatedAt = DateTime.Now
            };

            var user = await userManager.FindByEmailAsync(accountant.Email);

            if (user is null)
            {
                await userManager.CreateAsync(accountant, "P@ssword123");
                await userManager.AddToRoleAsync(accountant, AppRoles.Accountant);
            }
        }

        public static async Task SeedHolderUserAsync(UserManager<User> userManager)
        {
            User holder = new()
            {
                UserName = "holder@credit2.com",
                Email = "holder@credit2.com",
                Name = "Default Holder",
                Role = AppRoles.Holder,
                EmailConfirmed = true,
                CreatedAt = DateTime.Now
            };

            var user = await userManager.FindByEmailAsync(holder.Email);

            if (user is null)
            {
                await userManager.CreateAsync(holder, "P@ssword123");
                await userManager.AddToRoleAsync(holder, AppRoles.Holder);
            }
        }

        public static async Task SeedAuditorUserAsync(UserManager<User> userManager)
        {
            User auditor = new()
            {
                UserName = "auditor@credit2.com",
                Email = "auditor@credit2.com",
                Name = "Default Auditor",
                Role = AppRoles.Auditor,
                EmailConfirmed = true,
                CreatedAt = DateTime.Now
            };

            var user = await userManager.FindByEmailAsync(auditor.Email);

            if (user is null)
            {
                await userManager.CreateAsync(auditor, "P@ssword123");
                await userManager.AddToRoleAsync(auditor, AppRoles.Auditor);
            }
        }

        public static async Task SeedBoardMemberUserAsync(UserManager<User> userManager)
        {
            User boardMember = new()
            {
                UserName = "board@credit2.com",
                Email = "board@credit2.com",
                Name = "Default Board Member",
                Role = AppRoles.BoardMember,
                EmailConfirmed = true,
                CreatedAt = DateTime.Now
            };

            var user = await userManager.FindByEmailAsync(boardMember.Email);

            if (user is null)
            {
                await userManager.CreateAsync(boardMember, "P@ssword123");
                await userManager.AddToRoleAsync(boardMember, AppRoles.BoardMember);
            }
        }
    }
}