using Honeydew.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

namespace Honeydew.AuthenticationHandlers
{
    public class TokenAuthenticationHandler : AuthenticationHandler<TokenAuthenticationHandlerOptions>
    {
        private readonly ApplicationDbContext _context;

        public TokenAuthenticationHandler(
            IOptionsMonitor<TokenAuthenticationHandlerOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            ApplicationDbContext context)
            : base(options, logger, encoder, clock)
        {
            _context = context;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return AuthenticateResult.Fail("Unauthorized");
            }

            string authorizationHeader = Request.Headers["Authorization"];

            if (string.IsNullOrEmpty(authorizationHeader))
            {
                return AuthenticateResult.NoResult();
            }

            if (!authorizationHeader.StartsWith("token", StringComparison.OrdinalIgnoreCase))
            {
                return AuthenticateResult.Fail("Unauthorized");
            }

            string token = authorizationHeader.Substring("token".Length).Trim();

            if (string.IsNullOrEmpty(token))
            {
                return AuthenticateResult.Fail("Unauthorized");
            }

            try
            {
                return await ValidateToken(token, Context.RequestAborted);
            }
            catch (Exception ex)
            {
                return AuthenticateResult.Fail(ex.Message);
            }
        }

        private async Task<AuthenticateResult> ValidateToken(string token, CancellationToken cancellationToken)
        {
            var userToken = await _context.UserTokens.FirstOrDefaultAsync(
                x => x.LoginProvider == Scheme.Name && x.Value == token,
                cancellationToken);

            if (userToken == null)
            {
                return AuthenticateResult.Fail("Unauthorized");
            }

            var user = await _context.Users.FindAsync(new[] { userToken.UserId }, cancellationToken);

            var claims =
                new List<Claim>
                {
                    new Claim(ClaimTypes.Sid, user.Id),
                    new Claim(ClaimTypes.Name, user.UserName),
                };

            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }
}
