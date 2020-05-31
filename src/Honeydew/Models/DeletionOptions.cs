using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Honeydew.Models
{
    public class DeletionOptions
    {
        public bool AllowDeletionOfUploads { get; set; }
        public bool AlsoDeleteFileFromStorage { get; set; }
        public bool ScheduleAndMarkUploadsForDeletion { get; set; }
        public int DeleteSecondsAfterMarked { get; set; }
        public int RunCleanupEveryXSeconds { get; set; }
    }
}
