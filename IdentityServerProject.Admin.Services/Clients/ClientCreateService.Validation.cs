using System;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Clients;

public partial class ClientCreateService
{
    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        var isClientIdConstraint = message.Contains("IX_Clients_ClientId", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Clients.ClientId", StringComparison.OrdinalIgnoreCase);

        return isClientIdConstraint && UniqueConstraintViolationDetector.IsUniqueConstraintViolation(exception);
    }
}
