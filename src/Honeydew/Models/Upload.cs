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
        public string ContentType { get; set; }

        public long UploadedLength { get; set; }
        public long Length { get; set; }

        public string Metadata { get; set; }
        public string BlockIds { get; set; }
        public int? BlockNumber { get; set; }

        public string Url { get; set; }

        [MaxLength(20)]
        // This has a value converter to string in the database.
        public UploadStatus Status { get; set; }

        public string UserId { get; set; }
        public User User { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    }
}