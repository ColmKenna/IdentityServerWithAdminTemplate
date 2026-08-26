using System;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Users;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Secrets;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Secrets;

public sealed class SecretSecurityContextTests
{
    [Fact]
    public void Create_WithValidParameters_InitializesCorrectly()
    {
        var context = SecretSecurityContext.Create(
            new UserId("user-123"),
            SecretRevealPurpose.ClientCreated,
            "  client-app  ");

        Assert.Equal(new UserId("user-123"), context.ActorSubjectId);
        Assert.Equal(SecretRevealPurpose.ClientCreated, context.Purpose);
        Assert.Equal("client-app", context.TargetId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidTargetId_ThrowsArgumentException(string? invalidTargetId)
    {
        Assert.Throws<ArgumentException>(() =>
            SecretSecurityContext.Create(
                new UserId("user-123"),
                SecretRevealPurpose.ClientCreated,
                invalidTargetId!));
    }

    [Fact]
    public void CreateSecretCommand_InitializesProperties()
    {
        var expiration = DateTime.UtcNow.AddDays(30);
        var command = new CreateSecretCommand("client-a", "My Description", expiration);

        Assert.Equal("client-a", command.TargetId);
        Assert.Equal("My Description", command.Description);
        Assert.Equal(expiration, command.ExpirationUtc);
    }

    [Fact]
    public void AddApiResourceSecretCommand_ToCreateSecretCommand_MapsProperties()
    {
        var expiration = DateTime.UtcNow.AddDays(7);
        var apiSecretCmd = new AddApiResourceSecretCommand(ScopeName.Create("api-1"), "Api Secret", expiration);

        var unifiedCmd = apiSecretCmd.ToCreateSecretCommand();

        Assert.Equal("api-1", unifiedCmd.TargetId);
        Assert.Equal("Api Secret", unifiedCmd.Description);
        Assert.Equal(expiration, unifiedCmd.ExpirationUtc);
    }
}
