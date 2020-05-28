using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Honeydew.Helpers
{
    public static class CodeLanguageHelper
    {
        public static readonly string[] Languages =
            new[]
            {
                "abap",
                "apex",
                "azcli",
                "bat",
                "cameligo",
                "clojure",
                "coffee",
                "cpp",
                "csharp",
                "csp",
                "css",
                "dockerfile",
                "fsharp",
                "go",
                "graphql",
                "handlebars",
                "html",
                "ini",
                "java",
                "javascript",
                "kotlin",
                "less",
                "lua",
                "markdown",
                "mips",
                "msdax",
                "mysql",
                "objective-c",
                "pascal",
                "pascaligo",
                "perl",
                "pgsql",
                "php",
                "postiats",
                "powerquery",
                "powershell",
                "pug",
                "python",
                "r",
                "razor",
                "redis",
                "redshift",
                "restructuredtext",
                "ruby",
                "rust",
                "sb",
                "scheme",
                "scss",
                "shell",
                "solidity",
                "sophia",
                "sql",
                "st",
                "swift",
                "tcl",
                "twig",
                "typescript",
                "vb",
                "xml",
                "yaml",
            };

        public static IEnumerable<SelectListItem> GetLanguages(string selectedLanguage)
            => new[]
            {
                new SelectListItem("None", "", string.IsNullOrEmpty(selectedLanguage))
            }
            .Concat(Languages.Select(x => new SelectListItem(x, x, selectedLanguage == x)));
    }
}
