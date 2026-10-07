using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SifenInvoicing.Domain.PlatformAuth;
using SifenInvoicing.Infrastructure.Persistence;

namespace SifenInvoicing.Tests;

public sealed class TenantAuthorizationApiTests : IClassFixture<TenantAuthorizationApiTests.ApiFactory>
{
    private const string SigningKey = "test-signing-key-test-signing-key-0123456789";
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private readonly ApiFactory _factory;

    public TenantAuthorizationApiTests(ApiFactory factory) => _factory = factory;

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public ApiFactory()
        {
            Environment.SetEnvironmentVariable("SIFEN_JWT_SIGNING_KEY", SigningKey);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<SifenDbContext>>();
                var databaseName = Guid.NewGuid().ToString();
                services.AddDbContext<SifenDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        }
    }

    private HttpClient Client(string? token = null, Guid? tenantHeader = null)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (tenantHeader.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantHeader.Value.ToString());
        }

        return client;
    }

    private static string Token(Guid? tenantId, params string[] permissions)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) };
        if (tenantId.HasValue)
        {
            claims.Add(new Claim("tenantId", tenantId.Value.ToString()));
        }

        claims.AddRange(permissions.Select(p => new Claim("permission", p)));
        var jwt = new JwtSecurityToken(
            "SifenInvoicing.Api",
            "SifenInvoicing.Frontend",
            claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [Theory]
    [InlineData("POST", "/api/fe/invoices")]
    [InlineData("POST", "/invoice")]
    [InlineData("GET", "/invoice")]
    [InlineData("GET", "/api/fe/invoices/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/invoice/00000000-0000-0000-0000-000000000001/retry")]
    [InlineData("GET", "/api/fe/invoices/status/01444444017001001001452822017012515873260988")]
    [InlineData("GET", "/api/fe/plan/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/fe/diagnostic/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/fe/tenants/00000000-0000-0000-0000-000000000001/invoices")]
    public async Task InvoiceRoutes_ShouldReturn401_WithoutCredentials_EvenWithTenantHeader(string method, string path)
    {
        using var client = Client(tenantHeader: TenantA);
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonContent.Create(new { }) : null
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantHeader_ShouldBeRejected_WhenItDiffersFromAuthenticatedTenant()
    {
        using var client = Client(Token(TenantA, PlatformPermissions.InvoicesReadOwnTenant), tenantHeader: TenantB);
        var response = await client.GetAsync("/api/fe/invoices/00000000-0000-0000-0000-000000000001");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RouteTenant_ShouldBeForbidden_WhenItDiffersFromAuthenticatedTenant()
    {
        using var client = Client(Token(TenantA, PlatformPermissions.InvoicesReadOwnTenant));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/fe/tenants/{TenantB}/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/fe/tenants/{TenantB}/diagnostic")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/fe/plan/{TenantB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/fe/diagnostic/{TenantB}")).StatusCode);
    }

    [Fact]
    public async Task RouteTenant_ShouldBeAllowed_ForOwnTenant()
    {
        using var client = Client(Token(TenantA, PlatformPermissions.InvoicesReadOwnTenant));
        var response = await client.GetAsync($"/api/fe/tenants/{TenantA}/invoices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Issue_ShouldBeForbidden_ForReadOnlyUser()
    {
        using var client = Client(Token(TenantA, PlatformPermissions.InvoicesReadOwnTenant));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/fe/invoices", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/invoice", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync("/invoice/00000000-0000-0000-0000-000000000001/retry", null)).StatusCode);
    }

    [Fact]
    public async Task Read_ShouldBeForbidden_WithoutInvoicePermissions()
    {
        using var client = Client(Token(TenantA, PlatformPermissions.CompaniesUsersReadOwnTenant));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/invoice")).StatusCode);
    }

    [Fact]
    public async Task PlatformUserWithoutTenant_CannotReadTenantData_WithoutExplicitTenant()
    {
        using var client = Client(Token(null, PlatformPermissions.InvoicesIssueAnyTenant));
        var response = await client.GetAsync("/api/fe/invoices/00000000-0000-0000-0000-000000000001");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TenantUserWithoutAnyTenantPermission_CannotImpersonateWithHeaderWhenNoClaim()
    {
        using var client = Client(Token(null, PlatformPermissions.InvoicesReadOwnTenant), tenantHeader: TenantA);
        var response = await client.GetAsync("/api/fe/invoices/00000000-0000-0000-0000-000000000001");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
