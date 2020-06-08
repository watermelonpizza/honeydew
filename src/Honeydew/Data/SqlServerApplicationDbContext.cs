using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Honeydew.Data
{
    public class SqlServerApplicationDbContext : ApplicationDbContext
    {
        public SqlServerApplicationDbContext(DbContextOptions<SqlServerApplicationDbContext> options) : base(options)
        {
        }
    }
}
