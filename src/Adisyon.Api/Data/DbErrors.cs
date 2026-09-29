using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Data;

public static class DbErrors
{
    /// <summary>
    /// Kayıt, veritabanındaki bir tekillik kuralına takıldı mı? Ör. "bir masada tek açık adisyon":
    /// iki garson aynı anda aynı masayı açarsa ikincisi buraya düşer.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE };
}
