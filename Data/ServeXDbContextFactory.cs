using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServeX.Data
{
    public class ServeXDbContextFactory : IDesignTimeDbContextFactory<ServeXDbContext>
    {
        public ServeXDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ServeXDbContext>();
            optionsBuilder.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = ServeXDbContext.GetDatabaseFilePath() }.ToString());
            return new ServeXDbContext(optionsBuilder.Options);
        }
    }
}
