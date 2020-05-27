using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Honeydew.Models;
using Microsoft.AspNetCore.Identity;

namespace Honeydew.Models
{
    public class User : IdentityUser
    {
        public ICollection<Upload> Uploads { get; set; }
    }
}
