using L1bre.Journal.Data;
using L1bre.Journal.Domain.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Services;

/// <summary>
/// Runs once when the app starts: updates the database schema, creates the roles and,
/// while nobody is admin yet, prints an invitation link for the first admin in the logs.
/// </summary>
public static class StartupTasks
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        await sp.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count > 0) return;

        var adminEmail = config["App:AdminEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            logger.LogWarning("No admin account yet and App__AdminEmail is not set: nobody can be invited.");
            return;
        }

        // A fresh link at every start until the first admin has signed up. Older ones are revoked,
        // so only the link in the latest logs works.
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var old = await db.Invitations.Where(i => i.CreatedByUserId == null && i.UsedAt == null && i.RevokedAt == null).ToListAsync();
        old.ForEach(i => i.RevokedAt = now);
        await db.SaveChangesAsync();

        var created = await sp.GetRequiredService<InvitationService>()
            .CreateAsync(adminEmail, [Roles.Trader, Roles.Coach, Roles.Admin], createdByUserId: null);

        var baseUrl = (config["App:PublicUrl"] ?? "").TrimEnd('/');
        logger.LogWarning("FIRST ADMIN INVITATION for {Email}: {Url}/Account/Invite?token={Token}", adminEmail, baseUrl, created.Token);
    }
}
