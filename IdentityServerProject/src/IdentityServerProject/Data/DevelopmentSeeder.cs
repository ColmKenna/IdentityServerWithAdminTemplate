namespace IdentityServerProject.Data;

/// <summary>Guards seeding so it only ever runs in the Development environment.</summary>
public static class DevelopmentSeeder
{
    public static async Task SeedIfDevelopmentAsync(IHostEnvironment environment, Func<Task> seed)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        await seed();
    }
}
