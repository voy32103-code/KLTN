using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Data;

public static class BootstrapUserSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("BootstrapUsers:Enabled"))
            return;

        var accounts = new List<(string Email, string Name, string Password, UserRole Role)>();
        AddConfiguredAccount("Admin", UserRole.Admin);
        AddConfiguredAccount("Lecturer", UserRole.Lecturer);

        if (accounts.Count == 0)
            throw new InvalidOperationException(
                "BootstrapUsers is enabled but no complete bootstrap account is configured.");

        void AddConfiguredAccount(string sectionName, UserRole role)
        {
            var section = configuration.GetSection($"BootstrapUsers:{sectionName}");
            var email = section["Email"]?.Trim().ToLowerInvariant();
            var name = section["Name"]?.Trim();
            var password = section["Password"];

            if (string.IsNullOrWhiteSpace(email) &&
                string.IsNullOrWhiteSpace(name) &&
                string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"BootstrapUsers:{sectionName} must configure Email, Name, and Password together.");
            }

            if (password.Length < 12)
                throw new InvalidOperationException(
                    $"BootstrapUsers:{sectionName}:Password must be at least 12 characters.");

            accounts.Add((email, name, password, role));
        }

        var hasChanges = false;
        foreach (var account in accounts)
        {
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.Email == account.Email,
                cancellationToken);

            if (user is null)
            {
                db.Users.Add(new User
                {
                    Name = account.Name,
                    Email = account.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(account.Password),
                    Role = account.Role,
                    CreatedAt = DateTime.UtcNow
                });
                hasChanges = true;
                logger.LogInformation("Created configured bootstrap {Role} account.", account.Role);
            }
            else if (user.Role != account.Role)
            {
                user.Role = account.Role;
                hasChanges = true;
                logger.LogInformation("Updated configured bootstrap account role to {Role}.", account.Role);
            }
        }

        if (hasChanges)
            await db.SaveChangesAsync(cancellationToken);
    }
}
