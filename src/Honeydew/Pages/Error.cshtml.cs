using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Honeydew.Pages
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class ErrorModel : PageModel
    {
        public string RequestId { get; set; }

        public int TargetStatusCode { get; set; }
        public bool IsNotFound { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        private readonly ILogger<ErrorModel> _logger;

        public ErrorModel(ILogger<ErrorModel> logger)
        {
            _logger = logger;
        }

        public void OnGet(int? code)
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            TargetStatusCode = code ?? Response.StatusCode;
            IsNotFound = code == (int)HttpStatusCode.NotFound;
        }
    }
}
