using System;
using System.IO;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using tusdotnet.Models;

namespace Honeydew.Controllers
{
    [ApiController]
    [Authorize(AuthenticationSchemes = "Identity.Application,token")]
    public class UploadController : ControllerBase
    {
        private readonly IUploadStore _store;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IOptionsMonitor<DeletionOptions> _deletionOptions;
        private readonly SlugGenerator _slugGenerator;

        public UploadController(
            IUploadStore streamStore,
            ApplicationDbContext context,
            UserManager<User> userManager,
            IOptionsMonitor<DeletionOptions> deletionOptions,
            SlugGenerator slugGenerator)
        {
            _store = streamStore;
            _context = context;
            _userManager = userManager;
            _deletionOptions = deletionOptions;
            _slugGenerator = slugGenerator;
        }

        [HttpPost]
        [Route("api/upload")]
        public async Task<IActionResult> Upload(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return BadRequest("`filename` query parameter must be supplied");
            }

            var name = Path.GetFileNameWithoutExtension(filename);
            var extension = Path.GetExtension(filename);

            var upload = new Upload
            {
                Id = await _slugGenerator.GenerateSlugAsync(Request.HttpContext.RequestAborted),
                Name = name,
                Extension = extension,
                OriginalFileNameWithExtension = Path.GetFileName(filename),
                MediaType = MediaTypeHelpers.GetMediaTypeFromExtension(extension),
                Length = Request.ContentLength.GetValueOrDefault(),
                UserId = _userManager.GetUserId(User),
                CreatedBy = _userManager.GetUserName(User)
            };

            await _context.Uploads.AddAsync(upload, Request.HttpContext.RequestAborted);

            await _context.SaveChangesAsync(Request.HttpContext.RequestAborted);

            await _store.WriteAllBytesAsync(upload, Request.Body, Request.HttpContext.RequestAborted);

            return Ok(
                new JsonResult(
                    new
                    {
                        url = Url.PageLink("Upload", values: new { id = upload.Id })
                    }));
        }

        [HttpPatch]
        [Route("api/upload/{id}")]
        public async Task<IActionResult> PatchUpload(string id, PatchUploadModel uploadPatch)
        {
            var userId = _userManager.GetUserId(User);

            var upload = await _context.Uploads
                .FirstOrDefaultAsync(
                    x => x.Id == id && x.UserId == userId,
                    Request.HttpContext.RequestAborted);

            if (upload == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(uploadPatch.Name))
            {
                upload.Name = uploadPatch.Name;
            }

            if (!string.IsNullOrWhiteSpace(uploadPatch.CodeLanguage))
            {
                upload.CodeLanguage = uploadPatch.CodeLanguage;
            }

            await _context.SaveChangesAsync(Request.HttpContext.RequestAborted);

            return NoContent();
        }

        [HttpDelete]
        [Route("{id}")]
        public async Task<IActionResult> DeleteUpload(string id)
        {
            if (!_deletionOptions.CurrentValue.AllowDeletionOfUploads)
            {
                return Unauthorized();
            }

            var userId = _userManager.GetUserId(User);

            var upload = await _context.Uploads
                .FirstOrDefaultAsync(
                    x => x.Id == id && x.UserId == userId,
                    Request.HttpContext.RequestAborted);

            if (upload == null)
            {
                return NotFound();
            }

            if (!_deletionOptions.CurrentValue.ScheduleAndMarkUploadsForDeletion)
            {
                if (_deletionOptions.CurrentValue.AlsoDeleteFileFromStorage)
                {
                    await _store.DeleteAsync(upload, Request.HttpContext.RequestAborted);
                }

                _context.Uploads.Remove(upload);
            }
            else
            {
                if (upload.PendingForDeletionAt.HasValue)
                {
                    upload.PendingForDeletionAt = null;
                }
                else
                {
                    upload.PendingForDeletionAt = DateTimeOffset.UtcNow.AddSeconds(_deletionOptions.CurrentValue.DeleteSecondsAfterMarked);
                }
            }

            await _context.SaveChangesAsync(Request.HttpContext.RequestAborted);

            return NoContent();
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("{id}/raw")]
        public async Task<IActionResult> Raw(string id)
            => await GetFile(id, "inline");

        [HttpGet]
        [AllowAnonymous]
        [Route("{id}/download")]
        public async Task<IActionResult> Download(string id)
            => await GetFile(id, "attachment");

        private async Task<IActionResult> GetFile(string id, string contentDispositionType)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var upload = await _context.Uploads
                .FirstOrDefaultAsync(x => x.Id == id && !x.PendingForDeletionAt.HasValue, Request.HttpContext.RequestAborted);

            if (upload == null)
            {
                return NotFound();
            }

            var range = Request.GetTypedHeaders().Range;

            var stream = await _store.DownloadAsync(upload, range, Request.HttpContext.RequestAborted);

            Response.Headers.Add("Content-Disposition", $"{contentDispositionType};filename={upload.Name + upload.Extension}");

            if (MediaTypeHelpers.ParseMediaType(upload.MediaType) == MediaType.Text)
            {
                return File(stream, "text/plain", true);
            }
            else
            {
                return File(stream, upload.MediaType, true);
            }
        }
    }
}
