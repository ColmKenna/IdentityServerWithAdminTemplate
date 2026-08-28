using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Validation;

internal static class UniqueConstraintViolationDetector
{
    public static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sqlException && sqlException.Number is 2601 or 2627)
                return true;

        string message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}