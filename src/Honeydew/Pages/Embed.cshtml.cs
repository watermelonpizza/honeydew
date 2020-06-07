using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Helpers;
using Honeydew.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Honeydew.Pages
{
    public class EmbedModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public EmbedModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public string Id { get; set; }

        public Upload Upload { get; set; }

        public MediaType MediaTypeCategory => MediaTypeHelpers.ParseMediaType(Upload.MediaType);

        public async Task OnGetAsync()
        {
            Upload = await _context.Uploads
                .FirstOrDefaultAsync(x => x.Id == Id && !x.PendingForDeletionAt.HasValue, Request.HttpContext.RequestAborted);
        }
    }
}