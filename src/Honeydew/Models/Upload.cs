using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Honeydew.Models
{
    public class Upload
    {
        [Key]
        public string Id { get; set; }

        public string Name { get; set; }
        public string Extension { get; set; }
        public string OriginalFileNameWithExtension { get; set; }

        public string MediaType { get; set; }

        /// <summary>
        /// Only relevant for text mime type displays for monaco editor to render in.
        /// Must match the extact string that monaco knows about.
        /// List of languages supported here: https://github.com/microsoft/monaco-languages/tree/master/src
        /// </summary>
        [MaxLength(25)]
        public string CodeLanguage { get; set; }

        public long UploadedLength { get; set; }
        public long Length { get; set; }

        public string Metadata { get; set; }
        public string BlockIds { get; set; }
        public int? BlockNumber { get; set; }

        /// <summary>
        /// Third party upload id for temporary block uploads
        /// </summary>
        public string ProviderUploadId { get; set; }

        public string Url { get; set; }

        [MaxLength(20)]
        // This has a value converter to string in the database.
        public UploadStatus Status { get; set; }

        // This gives a grace period for deletions (if the user hits delete, but decides against later)
        // i.e. an "undo" button.
        public DateTimeOffset? PendingForDeletionAt { get; set; }

        public string UserId { get; set; }
        public User User { get; set; }

        public string CreatedBy { get; set; }
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    }
}