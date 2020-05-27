using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        public static MediaType ParseMediaType(string mediaType)
            => mediaType switch
            {
                "applcation/pdf" => MediaType.Application,

                var type when type.StartsWith("audio") => MediaType.Audio,
                
                var type when type.StartsWith("image") => MediaType.Image,
                
                var type when type.StartsWith("text") => MediaType.Text,
                
                var type when type.StartsWith("video") => MediaType.Video,
                
                _ => MediaType.UnknownOrNotSupported
            };
    }
}
