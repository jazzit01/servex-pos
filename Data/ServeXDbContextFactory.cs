using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServeX.Data
{
    public class ServeXDbContextFactory : IDesignTimeDbContextFactory<ServeXDbContext>
    {
        public ServeXDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ServeXDbContext>();
            optionsBuilder.UseSqlite("Data Source=" + ServeXDbContext.GetDatabaseFilePath());
            return new ServeXDbContext(optionsBuilder.Options);
        }
    }
}
