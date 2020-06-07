using Honeydew.Models;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Honeydew.Helpers
{
    public static class MediaTypeHelpers
    {
        public static string GetOpenGraphTypeFromMediaType(MediaType mediaType) =>
            mediaType switch
            {
                MediaType.Application => "website",
                MediaType.Audio => "music.song",
                MediaType.Font => "website",
                MediaType.Image => "article",
                MediaType.Model => "website",
                MediaType.Text => "website",
                MediaType.UnknownOrNotSupported => "website",
                MediaType.Video => "video.other",
                _ => "website"
            };

        public static string GetMediaTypeFromExtension(string extension)
        {
            var provider = new FileExtensionContentTypeProvider();
            provider.Mappings.Add(".sql", "application/sql");

            // TODO: Turn this into settings for custom mappings
            if (!provider.TryGetContentType(extension, out string mediaType))
            {
                mediaType = "application/octet-stream";
            }

            return mediaType;
        }

        public static string GetMediaTypeFromMetadata(Dictionary<string, tusdotnet.Models.Metadata> metadata)
        {
            if (!metadata.ContainsKey("mediaType")
                || metadata["mediaType"].HasEmptyValue
                || string.IsNullOrWhiteSpace(metadata["mediaType"].GetString(Encoding.UTF8)))
            {
                var extension = Path.GetExtension(metadata["name"].GetString(Encoding.UTF8));

                return GetMediaTypeFromExtension(extension);
            }

            return metadata["mediaType"].GetString(Encoding.UTF8);
        }

        public static MediaType ParseMediaType(string mediaType)
            => mediaType switch
            {
                "application/pdf" => MediaType.Application,
                "application/vnd.ms-excel" => MediaType.Text,
                "application/sql" => MediaType.Text,
                "application/json" => MediaType.Text,

                var type when type.StartsWith("audio") => MediaType.Audio,

                var type when type.StartsWith("image") => MediaType.Image,

                var type when type.StartsWith("text") => MediaType.Text,

                var type when type.StartsWith("video") => MediaType.Video,

                _ => MediaType.UnknownOrNotSupported
            };
    }
}
