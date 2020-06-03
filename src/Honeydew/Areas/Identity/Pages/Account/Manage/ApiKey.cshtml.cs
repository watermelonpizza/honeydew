using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Honeydew.AuthenticationHandlers;
using Honeydew.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Honeydew.Areas.Identity.Pages.Account.Manage
{
    public class ApiKeyModel : PageModel
    {
        private readonly UserManager<User> _userManager;

        public ApiKeyModel(UserManager<User> userManager)
        {
            _userManager = userManager;
        }

        public string ApiToken { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            ApiToken = await _userManager.GetAuthenticationTokenAsync(
                user,
                TokenAuthenticationHandler.TokenAuthenticationSchemeName,
                TokenAuthenticationHandler.TokenAuthenticationUserTokenName);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var result = await _userManager.SetAuthenticationTokenAsync(
                user,
                TokenAuthenticationHandler.TokenAuthenticationSchemeName,
                TokenAuthenticationHandler.TokenAuthenticationUserTokenName,
                _userManager.GenerateNewAuthenticatorKey());

            if (!result.Succeeded)
            {
                ErrorMessage = string.Join(", ", result.Errors.Select(x => x.Description));
            }
            else
            {
                ApiToken = await _userManager.GetAuthenticationTokenAsync(
                    user,
                    TokenAuthenticationHandler.TokenAuthenticationSchemeName,
                    TokenAuthenticationHandler.TokenAuthenticationUserTokenName);
            }

            return Page();
        }
    }
}
