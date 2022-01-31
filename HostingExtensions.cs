using Duende.IdentityServer;
using identity_ef;
using identity_ef.Pages.Admin.ApiScopes;
using identity_ef.Pages.Admin.Clients;
using identity_ef.Pages.Admin.IdentityScopes;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;

namespace identity_ef;

internal static class HostingExtensions
{
    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddRazorPages();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

        builder.Services
            .AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;

                // see https://docs.duendesoftware.com/identityserver/v5/fundamentals/resources/
                options.EmitStaticAudienceClaim = true;
            })
            .AddTestUsers(TestUsers.Users)
            // this adds the config data from DB (clients, resources, CORS)
            .AddConfigurationStore(options =>
            {
                options.ConfigureDbContext = b =>
                    b.UseSqlite(connectionString, dbOpts => dbOpts.MigrationsAssembly(typeof(Program).Assembly.FullName));
            })
            // this is something you will want in production to reduce load on and requests to the DB
            //.AddConfigurationStoreCache()
            //
            // this adds the operational data from DB (codes, tokens, consents)
            .AddOperationalStore(options =>
            {
                options.ConfigureDbContext = b =>
                    b.UseSqlite(connectionString, dbOpts => dbOpts.MigrationsAssembly(typeof(Program).Assembly.FullName));

                // this enables automatic token cleanup. this is optional.
                options.EnableTokenCleanup = true;
                options.RemoveConsumedTokens = true;
            })
            .AddSamlDynamicProvider(optionsAugmenter =>{
                optionsAugmenter.LicenseKey = "eyJTb2xkRm9yIjowLjAsIktleVByZXNldCI6NiwiU2F2ZUtleSI6ZmFsc2UsIkxlZ2FjeUtleSI6ZmFsc2UsIlJlbmV3YWxTZW50VGltZSI6IjAwMDEtMDEtMDFUMDA6MDA6MDAiLCJhdXRoIjoiREVNTyIsImV4cCI6IjIwMjItMDItMjRUMDE6MDA6MDEuNzk2MTgzMiswMDowMCIsImlhdCI6IjIwMjItMDEtMjVUMDE6MDA6MDEiLCJvcmciOiJERU1PIiwiYXVkIjo0fQ==.Y+gpU39VZUK84OJ5qC2Yh7hBecbPM2doKFp7nPFFfE5E9APTOdbAaHiTwLZrnmIjTarcphFYhWZ5yvcPuOy9+XmOtnxYCWx8ZNQGVudjY0LmaHDupg7qhSGqEFyNBPjJTq7lc5//HfTFG0XXaqIhlsGNhR6laqKVvgDrlQgIL3qISste2gUJCgnsDqjOjyx6wrYLv1bYQfuZv5ymWA8kgjYYXlYdZ7PJdqfOLhEYk8oxn9OInKPigyZhW5TJM7zKuYZvRk/6qgLpPnuXJI42w/62izhkHOVikORNb4HW92h+NL81ASyYn7VOsbYiKQJHafL/ql7Y5E3O6cCEGvkhTeUOfCSKafbxAF0pnSQF6L8oaQV7jSWjrd1KJfxZbhZE04hSSaWqsxYqx03QbqATa3rMdDnS8rUfReV7ZBVXcY4MBbXYOn7ncomfaSJeE0Y2WeV7RdikDX9/ElHAJy5amNRirDNHpaPfoCSlTVFm/1Bjr2mMWHuDZzqbGCDksuKjHYY+SOQRDaj8TWTSqhYa31qCNfwH+5oeXu/UlcX3vXhX+dn+1ViGwaYRDjFvS9DmehhH1X8DdVryurJ5IiANNL3GaNzvEywOFbjy2UFVrONws1Pxa/22C80xBRyoFFpDQI3cOdqNkYbvXNHKz8o18EIiKfLVDECYxI3SQvxbIRU=";
                optionsAugmenter.Licensee = "DEMO";

            });
            builder.Services.AddLogging(options =>
            {
                options.AddFilter("Duende", LogLevel.Information);
            });
        // this adds the necessary config for the simple admin/config pages
        {
            builder.Services.AddAuthorization(options =>
                options.AddPolicy("admin",
                    policy => policy.RequireClaim("sub", "1"))
            );

            builder.Services.Configure<RazorPagesOptions>(options =>
                options.Conventions.AuthorizeFolder("/Admin", "admin"));

            builder.Services.AddTransient<ClientRepository>();
            builder.Services.AddTransient<IdentityScopeRepository>();
            builder.Services.AddTransient<ApiScopeRepository>();
        }

        return builder.Build();
    }
    
    public static WebApplication ConfigurePipeline(this WebApplication app)
    { 
        app.UseSerilogRequestLogging();
    
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        InitializeDatabase(app);
        app.UseStaticFiles();
        app.UseRouting();
        app.UseIdentityServer();
        app.UseAuthorization();
        
        app.MapRazorPages()
            .RequireAuthorization();

        return app;
    }
    private static void InitializeDatabase(IApplicationBuilder app)
{
    using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
    {
        serviceScope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>().Database.Migrate();

        var context = serviceScope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        context.Database.Migrate();
        if (!context.Clients.Any())
        {
            foreach (var client in Config.Clients)
            {
                context.Clients.Add(client.ToEntity());
            }
            context.SaveChanges();
        }

        if (!context.IdentityResources.Any())
        {
            foreach (var resource in Config.IdentityResources)
            {
                context.IdentityResources.Add(resource.ToEntity());
            }
            context.SaveChanges();
        }

        if (!context.ApiScopes.Any())
        {
            foreach (var resource in Config.ApiScopes)
            {
                context.ApiScopes.Add(resource.ToEntity());
            }
            context.SaveChanges();
        }
    }
}
}