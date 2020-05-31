using System;
using System.IO;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using tusdotnet.Models;

namespace Honeydew.Controllers
{
    [Authorize]
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly IStreamStore _streamStore;
        private readonly DefaultTusConfiguration _defaultTusConfiguration;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IOptionsMonitor<DeletionOptions> _deletionOptions;

        public UploadController(
            IStreamStore streamStore,
            DefaultTusConfiguration defaultTusConfiguration,
            ApplicationDbContext context,
            UserManager<User> userManager,
            IOptionsMonitor<DeletionOptions> deletionOptions)
        {
            _streamStore = streamStore;
            _defaultTusConfiguration = defaultTusConfiguration;
            _context = context;
            _userManager = userManager;
            _deletionOptions = deletionOptions;
        }

        [HttpPost]
        [Route("api/upload")]
        public async Task<IActionResult> Upload(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return BadRequest("`filename` query parameter must be supplied");
            }

            filename = Path.GetFileName(filename);

            await _streamStore.WriteAllBytesAsync(filename, Request.Body, Request.HttpContext.RequestAborted);

            return Ok();
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
                    await (_defaultTusConfiguration.Store as IHoneydewTusStore)
                        .DeleteFileAsync(id, Request.HttpContext.RequestAborted);
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

            var uploadFile =
                await (_defaultTusConfiguration.Store as IHoneydewTusStore).GetFileAsync(id, Request.HttpContext.RequestAborted);

            Response.Headers.Add("Content-Disposition", $"{contentDispositionType};filename={upload.Name + upload.Extension}");

            if (MediaTypeHelpers.ParseMediaType(upload.MediaType) == MediaType.Text)
            {
                return File(await uploadFile.GetContentAsync(Request.HttpContext.RequestAborted), "text/plain", true);
            }
            else
            {
                return File(await uploadFile.GetContentAsync(Request.HttpContext.RequestAborted), upload.MediaType, true);
            }
        }
    }
}
