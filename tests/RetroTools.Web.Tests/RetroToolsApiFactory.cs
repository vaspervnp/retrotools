using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetroTools.Data;
using RetroTools.Data.Entities;

namespace RetroTools.Web.Tests;

/// <summary>
/// Σηκώνει την πραγματική εφαρμογή στη μνήμη, με τη <b>πραγματική</b> MariaDB από
/// πίσω. Αντικαθίσταται μόνο η ταυτοποίηση — όλα τα υπόλοιπα (routing, φίλτρα,
/// validation, serialization) δοκιμάζονται όπως είναι.
/// </summary>
public sealed class RetroToolsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly List<Guid> _createdUsers = new List<Guid>();

    public bool IsAvailable { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development ώστε να φορτωθούν τα user secrets με το connection string.
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    public Task InitializeAsync()
    {
        IsAvailable = TestConfiguration.HasDatabase;
        return Task.CompletedTask;
    }

    /// <summary>Client που μιλά ως συγκεκριμένος χρήστης· <c>null</c> = ανώνυμος.</summary>
    public HttpClient CreateClientAs(Guid? userId)
    {
        var client = CreateClient();

        if (userId.HasValue)
        {
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, userId.Value.ToString());
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    public async Task<Guid> CreateUserAsync(string displayName)
    {
        using var scope = Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RetroToolsDbContext>>();
        await using var context = new RetroToolsDbContext(options, SystemUser.Instance);

        var user = new User { Id = Guid.NewGuid(), DisplayName = displayName };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        lock (_createdUsers)
        {
            _createdUsers.Add(user.Id);
        }

        return user.Id;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Guid[] ids;
        lock (_createdUsers)
        {
            ids = _createdUsers.ToArray();
            _createdUsers.Clear();
        }

        if (ids.Length > 0 && IsAvailable)
        {
            using var scope = Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RetroToolsDbContext>>();
            await using var context = new RetroToolsDbContext(options, SystemUser.Instance);

            await context.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<RetroToolsApiFactory>
{
    public const string Name = "api";
}
