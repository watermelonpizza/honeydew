using Microsoft.AspNetCore.StaticFiles;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using tusdotnet.Models;

namespace Honeydew.Models
{
    public enum MediaType
    {
        Application,
        Audio,
        Font,
        Image,
        Model,
        Text,
        Video,
        UnknownOrNotSupported
    }

    public static class MediaTypeHelpers
    {
        public static string GetMediaTypeFromMetadata(Dictionary<string, tusdotnet.Models.Metadata> metadata)
        {
            if (!metadata.ContainsKey("mediaType")
                || metadata["mediaType"].HasEmptyValue
                || string.IsNullOrWhiteSpace(metadata["mediaType"].GetString(Encoding.UTF8)))
            {
                var provider = new FileExtensionContentTypeProvider();
                provider.Mappings.Add(".sql", "application/sql");

                var extension = Path.GetExtension(metadata["name"].GetString(Encoding.UTF8));
                // TODO: Turn this into settings for custom mappings
                if (!provider.TryGetContentType(extension, out string mediaType))
                {
                    mediaType = "application/octet-stream";
                }

                return mediaType;
            }

            return metadata["mediaType"].GetString(Encoding.UTF8);
        }

        public static MediaType ParseMediaType(string mediaType)
            => mediaType switch
            {
                "application/pdf" => MediaType.Application,
                "application/vnd.ms-excel" => MediaType.Text,
                "application/sql" => MediaType.Text,

                var type when type.StartsWith("audio") => MediaType.Audio,

                var type when type.StartsWith("image") => MediaType.Image,

                var type when type.StartsWith("text") => MediaType.Text,

                var type when type.StartsWith("video") => MediaType.Video,

                _ => MediaType.UnknownOrNotSupported
            };
    }
}
