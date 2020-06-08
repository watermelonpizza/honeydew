using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Honeydew.Data
{
    public class SqlServerDesignTimeContextFactory : IDesignTimeDbContextFactory<SqlServerApplicationDbContext>
    {
        public SqlServerApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<SqlServerApplicationDbContext>();
            optionsBuilder.UseSqlServer(
                @"Server=(localdb)\\mssqllocaldb;Database=honeydew;Trusted_Connection=True;MultipleActiveResultSets=true");

            return new SqlServerApplicationDbContext(optionsBuilder.Options);
        }
    }
}
