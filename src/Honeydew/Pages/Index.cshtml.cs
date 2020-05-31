using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Honeydew.Data;
using Honeydew.Models;
using Honeydew.UploadStores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Honeydew.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IStreamStore _streamStore;
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly DefaultTusConfiguration _defaultTusConfiguration;
        private readonly IOptionsMonitor<DeletionOptions> _delectionOptions;

        public IndexModel(
            ILogger<IndexModel> logger,
            IStreamStore streamStore,
            ApplicationDbContext context,
            UserManager<User> userManager,
            DefaultTusConfiguration defaultTusConfiguration,
            IOptionsMonitor<DeletionOptions> delectionOptions)
        {
            _logger = logger;
            _streamStore = streamStore;
            _context = context;
            _userManager = userManager;
            _defaultTusConfiguration = defaultTusConfiguration;
            _delectionOptions = delectionOptions;
        }

        public List<Upload> UserUploads { get; set; }

        public bool AllowDeletion { get; set; }

        public bool ScheduledDeletion { get; set; }

        public int ScheduledDeletionTime { get; set; }

        public async Task OnGet()
        {
            AllowDeletion = _delectionOptions.CurrentValue.AllowDeletionOfUploads;
            ScheduledDeletion = _delectionOptions.CurrentValue.ScheduleAndMarkUploadsForDeletion;
            ScheduledDeletionTime = _delectionOptions.CurrentValue.DeleteSecondsAfterMarked;

            var userId = _userManager.GetUserId(User);

            UserUploads =
                await _context.Uploads
                .Where(x => x.UserId == userId)
                .Where(x => !x.PendingForDeletionAt.HasValue)
                .Take(10)
                .OrderByDescending(x => x.CreatedUtc)
                .ToListAsync();
        }
    }
}
