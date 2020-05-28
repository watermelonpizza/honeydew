using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.EventLog;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Honeydew.Controllers
{
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly IStreamStore _streamStore;
        private readonly DefaultTusConfiguration _defaultTusConfiguration;
        private readonly ApplicationDbContext _context;

        public UploadController(IStreamStore streamStore, DefaultTusConfiguration defaultTusConfiguration, ApplicationDbContext context)
        {
            _streamStore = streamStore;
            _defaultTusConfiguration = defaultTusConfiguration;
            _context = context;
        }

        [HttpPost]
        [Authorize]
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

        [HttpGet]
        [Route("{id}/raw")]
        public async Task<IActionResult> Raw(string id)
            => await GetFile(id, "inline");

        [HttpGet]
        [Route("{id}/download")]
        public async Task<IActionResult> Download(string id)
            => await GetFile(id, "attachment");

        private async Task<IActionResult> GetFile(string id, string contentDispositionType)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var upload = await _context.Uploads.FindAsync(new[] { id }, Request.HttpContext.RequestAborted);

            if (upload == null)
            {
                return NotFound();
            }

            var uploadFile =
                await (_defaultTusConfiguration.Store as ITusReadableStore).GetFileAsync(id, Request.HttpContext.RequestAborted);

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
