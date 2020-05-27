using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Honeydew.Pages
{
    public class ViewUploadModel : PageModel
    {
        private ApplicationDbContext _context;

        public ViewUploadModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public string Id { get; set; }

        public Upload Upload { get; set; }

        public bool IsImage { get; set; }

        public bool IsVideo { get; set; }

        public string UploadUrl { get; set; }

        public async Task OnGet()
        {
            Upload = await _context.Uploads.FindAsync(new[] { Id }, Request.HttpContext.RequestAborted);

            IsImage = Upload?.ContentType?.StartsWith("image") ?? false;
        }
    }
}