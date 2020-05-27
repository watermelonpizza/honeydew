using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Honeydew.Exceptions
{
    [Serializable]
    public class StorageConfigException : Exception
    {
        public StorageConfigException(string type, string configName, string configValue, string customValueError = null, params string[] configValueExamples)
        : base($"Invalid storage config for type `{type}`: `{configName}` was set to `{configValue}` {customValueError ?? "but wasn't recognised or is invalid"}{(configValueExamples.Any() ? $" valid options may include `{string.Join("`, `", configValueExamples)}` etc." : string.Empty)}")
        {
        }
    }
}
