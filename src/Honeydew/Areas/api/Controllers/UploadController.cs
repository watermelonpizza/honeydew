using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Honeydew.Areas.api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly IStreamStore _streamStore;

        public UploadController(IStreamStore streamStore)
        {
            _streamStore = streamStore;
        }

        [Route("")]
        [HttpPost]
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
    }
}
