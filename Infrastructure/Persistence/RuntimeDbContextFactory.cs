using Microsoft.EntityFrameworkCore;
using ServeX.Data;

namespace ServeX.Infrastructure.Persistence
{
    public sealed class RuntimeDbContextFactory : IDbContextFactory<ServeXDbContext>
    {
        private readonly DbContextOptions<ServeXDbContext> _options;
        public RuntimeDbContextFactory(DatabaseLocation location)
        {
            _options = new DbContextOptionsBuilder<ServeXDbContext>()
                .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = location.FilePath }.ToString())
                .Options;
        }
        public ServeXDbContext CreateDbContext() => new ServeXDbContext(_options);
    }
}
