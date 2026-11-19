using System.Text.RegularExpressions;
using IdentityService.Application.Interfaces;
using IdentityService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IdentityService.Infrastructure.Persistence;

/// <summary>Provisions the first administrator when explicit bootstrap credentials are supplied.</summary>
public static class AdminBootstrapper
{
    private static readonly Guid LegacyDemoAdminId = Guid.Parse("33333333-0000-0000-0000-000000000001");

    public static async Task ProvisionAsync(UserDbContext db, IPasswordHasher hasher,
        IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var phone = configuration["Identity:AdminBootstrap:Phone"]?.Trim();
        var password = configuration["Identity:AdminBootstrap:Password"];
        var legacyDemo = await db.Users.SingleOrDefaultAsync(
            u => u.Id == LegacyDemoAdminId && u.RoleId == Role.AdminId &&
                 u.PasswordHash == SeedData.DemoPasswordHash, cancellationToken);
        if (string.IsNullOrEmpty(phone) && string.IsNullOrEmpty(password))
        {
            if (legacyDemo is { IsActive: true })
            {
                legacyDemo.IsActive = false;
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }
        if (string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(password) ||
            !Regex.IsMatch(phone, @"^0\d{9}$") || password.Length < 12)
            throw new InvalidOperationException(
                "Identity:AdminBootstrap requires a 10-digit phone and a password of at least 12 characters.");

        var existing = await db.Users.SingleOrDefaultAsync(u => u.Phone == phone, cancellationToken);
        if (existing is not null)
        {
            if (existing.RoleId != Role.AdminId)
                throw new InvalidOperationException("Admin bootstrap phone belongs to a non-admin user.");
            if (existing.Id == LegacyDemoAdminId &&
                existing.PasswordHash == SeedData.DemoPasswordHash)
            {
                existing.PasswordHash = hasher.Hash(password);
                existing.IsActive = true;
            }
            if (legacyDemo is not null && legacyDemo.Id != existing.Id)
                legacyDemo.IsActive = false;
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (legacyDemo is not null)
            legacyDemo.IsActive = false;

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            RoleId = Role.AdminId,
            FullName = "Quản trị viên",
            Phone = phone,
            PasswordHash = hasher.Hash(password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another Identity instance may have provisioned the same account concurrently.
            db.ChangeTracker.Clear();
            if (!await db.Users.AnyAsync(u => u.Phone == phone && u.RoleId == Role.AdminId,
                    cancellationToken))
                throw;
        }
    }
}
