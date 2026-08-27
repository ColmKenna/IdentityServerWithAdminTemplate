using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Roles;
using IdentityServerProject.Services.Validation;
using Moq;

namespace IdentityServerProject.Admin.Tests.Roles;

public class RoleServiceTests
{
    private readonly Mock<IAuditWriter> _auditWriterMock = new();
    private readonly Mock<IRoleAdministrationStore> _storeMock = new();
    private readonly RoleService _sut;

    public RoleServiceTests()
    {
        _sut = new RoleService(_storeMock.Object, _auditWriterMock.Object);
    }

    [Fact]
    public async Task GetRolesAsync_DelegatesToStore()
    {
        var expected = new ListResult<RoleListItem>
        {
            Items = new List<RoleListItem>
            {
                new() { Id = RoleId.Create("1"), Name = "SysAdmin", IsProtected = true },
                new() { Id = RoleId.Create("2"), Name = "Editor", IsProtected = false }
            },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        };

        _storeMock.Setup(s =>
                s.GetRolesAsync(new ListQuery("edit", Pagination.From(1, 10)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        ListResult<RoleListItem> result = await _sut.GetRolesAsync(new ListQuery("edit", Pagination.From(1, 10)));

        Assert.Same(expected, result);
        _storeMock.Verify(
            s => s.GetRolesAsync(new ListQuery("edit", Pagination.From(1, 10)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRoleAsync_DelegatesToStore()
    {
        var expected = new RoleDetailsModel { Id = RoleId.Create("1"), Name = "SysAdmin", IsProtected = true };
        _storeMock.Setup(s => s.FindRoleAsync(RoleId.Create("1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        RoleDetailsModel? result = await _sut.GetRoleAsync(RoleId.Create("1"));

        Assert.Same(expected, result);
        _storeMock.Verify(s => s.FindRoleAsync(RoleId.Create("1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRoleAsync_SuccessfulCreation_WritesSucceededAudit()
    {
        var input = new RoleCreateInputModel { Name = "Manager" };
        _storeMock.Setup(s => s.CreateRoleAsync(input, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleCreateOutcome.Succeeded, RoleId.Create("role-123"), null));

        RoleCreateResult result = await _sut.CreateRoleAsync(input);

        Assert.True(result.Success);
        Assert.Equal(RoleId.Create("role-123"), result.RoleId);

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Create &&
                e.Outcome == AuditOutcome.Succeeded &&
                e.ReasonCode == AuditReasonCode.Succeeded &&
                e.TargetId == "role-123" &&
                e.TargetName == "Manager"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRoleAsync_NameCollision_WritesDeniedAudit()
    {
        var input = new RoleCreateInputModel { Name = "SysAdmin" };
        _storeMock.Setup(s => s.CreateRoleAsync(input, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleCreateOutcome.NameCollision, null, null));

        RoleCreateResult result = await _sut.CreateRoleAsync(input);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Create &&
                e.Outcome == AuditOutcome.Denied &&
                e.ReasonCode == AuditReasonCode.NameCollision &&
                e.TargetName == "SysAdmin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRoleAsync_ValidationFailed_WritesDeniedAudit()
    {
        var input = new RoleCreateInputModel { Name = "BadRole" };
        _storeMock.Setup(s => s.CreateRoleAsync(input, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleCreateOutcome.ValidationFailed, null, "Invalid role name"));

        RoleCreateResult result = await _sut.CreateRoleAsync(input);

        Assert.False(result.Success);
        Assert.Equal("Invalid role name", result.ErrorMessage);

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Create &&
                e.Outcome == AuditOutcome.Denied &&
                e.ReasonCode == AuditReasonCode.ValidationFailed &&
                e.TargetName == "BadRole"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRoleAsync_Exception_WritesFailedAuditAndRethrows()
    {
        var input = new RoleCreateInputModel { Name = "ExceptionRole" };
        _storeMock.Setup(s => s.CreateRoleAsync(input, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateRoleAsync(input));

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Create &&
                e.Outcome == AuditOutcome.Failed &&
                e.ReasonCode == AuditReasonCode.PersistenceFailure &&
                e.TargetName == "ExceptionRole"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRoleAsync_SuccessfulDeletion_WritesSucceededAudit()
    {
        _storeMock.Setup(s => s.DeleteRoleAsync(RoleId.Create("role-1"), "SysAdmin", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDeleteOutcome.Succeeded, "Auditor"));

        AdminMutationResult result = await _sut.DeleteRoleAsync(RoleId.Create("role-1"));

        Assert.True(result.Succeeded);

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Delete &&
                e.Outcome == AuditOutcome.Succeeded &&
                e.ReasonCode == AuditReasonCode.Succeeded &&
                e.TargetId == "role-1" &&
                e.TargetName == "Auditor"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRoleAsync_ProtectedRole_WritesDeniedAudit()
    {
        _storeMock
            .Setup(s => s.DeleteRoleAsync(RoleId.Create("sysadmin-id"), "SysAdmin", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDeleteOutcome.ProtectedRoleBlocked, "SysAdmin"));

        AdminMutationResult result = await _sut.DeleteRoleAsync(RoleId.Create("sysadmin-id"));

        Assert.False(result.Succeeded);
        Assert.Contains("protected role", result.ErrorMessage);

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Delete &&
                e.Outcome == AuditOutcome.Denied &&
                e.ReasonCode == AuditReasonCode.ProtectedResource &&
                e.TargetId == "sysadmin-id" &&
                e.TargetName == "SysAdmin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRoleAsync_NotFound_WritesDeniedAudit()
    {
        _storeMock.Setup(s => s.DeleteRoleAsync(RoleId.Create("missing-id"), "SysAdmin", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDeleteOutcome.RoleNotFound, "missing-id"));

        AdminMutationResult result = await _sut.DeleteRoleAsync(RoleId.Create("missing-id"));

        Assert.False(result.Succeeded);
        Assert.Equal("Role not found.", result.ErrorMessage);

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Delete &&
                e.Outcome == AuditOutcome.Denied &&
                e.ReasonCode == AuditReasonCode.NotFound &&
                e.TargetId == "missing-id"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRoleAsync_ValidationFailed_WritesDeniedAudit()
    {
        _storeMock.Setup(s => s.DeleteRoleAsync(RoleId.Create("bad-id"), "SysAdmin", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDeleteOutcome.ValidationFailed, "bad-role"));

        AdminMutationResult result = await _sut.DeleteRoleAsync(RoleId.Create("bad-id"));

        Assert.False(result.Succeeded);
        Assert.Equal("Failed to delete role.", result.ErrorMessage);

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Delete &&
                e.Outcome == AuditOutcome.Denied &&
                e.ReasonCode == AuditReasonCode.ValidationFailed &&
                e.TargetId == "bad-id" &&
                e.TargetName == "bad-role"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRoleAsync_Exception_WritesFailedAuditAndRethrows()
    {
        _storeMock.Setup(s => s.DeleteRoleAsync(RoleId.Create("ex-id"), "SysAdmin", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteRoleAsync(RoleId.Create("ex-id")));

        _auditWriterMock.Verify(a => a.WriteAsync(
            It.Is<AdminAuditEvent>(e =>
                e.Category == AuditCategory.Role &&
                e.Action == AuditAction.Delete &&
                e.Outcome == AuditOutcome.Failed &&
                e.ReasonCode == AuditReasonCode.PersistenceFailure &&
                e.TargetId == "ex-id"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}