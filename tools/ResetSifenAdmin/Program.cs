using Microsoft.EntityFrameworkCore;
using SifenInvoicing.Infrastructure.Auth;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Tenancy;

var email = args.ElementAtOrDefault(0) ?? "admin@sifen.local";
var password = args.ElementAtOrDefault(1) ?? "Admin123!";
var connectionString = args.ElementAtOrDefault(2)
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=SifenInvoicingDev;Trusted_Connection=True;TrustServerCertificate=True";

var options = new DbContextOptionsBuilder<SifenDbContext>()
    .UseSqlServer(connectionString)
    .Options;

await using var dbContext = new SifenDbContext(
    options,
    new SystemClock(),
    new AsyncLocalTenantContextAccessor());

var hasher = new Pbkdf2PasswordHasher();
var passwordHash = hasher.Hash(password);
var normalizedEmail = email.Trim().ToLowerInvariant();

var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
    $"UPDATE PlatformUsers SET PasswordHash = {passwordHash}, IsActive = 1 WHERE Email = {normalizedEmail}");

Console.WriteLine(updated == 0
    ? $"No PlatformUser found for {normalizedEmail}."
    : $"Password reset for {normalizedEmail}.");
