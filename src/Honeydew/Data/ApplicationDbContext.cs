using System;
using System.Collections.Generic;
using System.Text;
using Honeydew.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Honeydew.Data
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Upload>()
                .Property(x => x.Status)
                .HasConversion(
                    status => status.ToString(),
                    status => Enum.Parse<UploadStatus>(status, true));

            builder.Entity<User>()
                .HasMany(x => x.Uploads)
                .WithOne(x => x.User)
                .OnDelete(DeleteBehavior.ClientSetNull);
        }

        public DbSet<Upload> Uploads { get; set; }
    }
}
