using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Honeydew.Models
{
    public class UploadRenderModel
    {
        private readonly Upload _upload;

        public string Id => _upload.Id;

        public string MediaType => _upload.MediaType;

        public MediaType MediaTypeCategory => MediaTypeHelpers.ParseMediaType(_upload.MediaType);

        public UploadRenderModel(Upload upload)
        {
            _upload = upload;
        }
    }
}
