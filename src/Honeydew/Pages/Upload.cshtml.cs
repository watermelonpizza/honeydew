using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using tusdotnet.Models;

namespace Honeydew.Pages
{
    public class UploadModel : PageModel
    {
        private ApplicationDbContext _context;

        public UploadModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public string Id { get; set; }

        public Upload Upload { get; set; }

        public MediaType MediaTypeCategory => MediaTypeHelpers.ParseMediaType(Upload.MediaType);

        public async Task OnGet()
        {
            Upload = await _context.Uploads.FindAsync(new[] { Id }, Request.HttpContext.RequestAborted);
        }
    }
}