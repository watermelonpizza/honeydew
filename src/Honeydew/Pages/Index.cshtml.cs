using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Honeydew.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IStreamStore _streamStore;

        public IndexModel(
            ILogger<IndexModel> logger,
            IStreamStore streamStore)
        {
            _logger = logger;
            _streamStore = streamStore;
        }

        public void OnGet()
        {

        }
    }
}
