using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Honeydew.AuthenticationHandlers;
using Honeydew.Models;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Honeydew.Areas.Identity.Pages.Account.Manage
{
    public class SharexConfigModel : PageModel
    {
        private readonly UserManager<User> _userManager;

        public SharexConfigModel(UserManager<User> userManager)
        {
            _userManager = userManager;
        }

        public string ShareXConfig { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            ShareXConfig = await GetConfig(user);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var config = await GetConfig(user);

            Response.Headers.Add("Content-Disposition", $"attachment;filename=honeydew.sxcu");

            return File(Encoding.UTF8.GetBytes(config), "text/plain");
        }

        private async Task<string> GetConfig(User user)
        {
            var token = await _userManager.GetAuthenticationTokenAsync(
                user,
                TokenAuthenticationHandler.TokenAuthenticationSchemeName,
                TokenAuthenticationHandler.TokenAuthenticationUserTokenName) ?? "<your api token>";

            var config = new
            {
                Version = "13.1.0",
                Name = "honewdew",
                DestinationType = "ImageUploader, TextUploader, FileUploader",
                RequestMethod = "POST",
                RequestURL = Url.ActionLink("Upload", "Upload"),
                Parameters = new { filename = "$filename$" },
                Headers = new { Authorization = "Token " + token },
                Body = "Binary",
                URL = "$json:url$"
            };

            return JsonConvert.SerializeObject(config, new JsonSerializerSettings { Formatting = Formatting.Indented });
        }
    }
}
