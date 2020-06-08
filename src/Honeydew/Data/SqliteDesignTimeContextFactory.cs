using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Honeydew.Data
{
    public class SqliteDesignTimeContextFactory : IDesignTimeDbContextFactory<SqliteApplicationDbContext>
    {
        public SqliteApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<SqliteApplicationDbContext>();
            optionsBuilder.UseSqlite("DataSource=app.db");

            return new SqliteApplicationDbContext(optionsBuilder.Options);
        }
    }
}
