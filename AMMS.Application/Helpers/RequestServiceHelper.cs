using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.Helpers;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Helpers
{
    public class RequestServiceHelper
    {
        public static string ResolveContentType(string fileName, string? contentType)
        {
            if (!string.IsNullOrWhiteSpace(contentType) &&
                !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return contentType;
            }

            var provider = new FileExtensionContentTypeProvider();
            if (provider.TryGetContentType(fileName, out var mapped))
                return mapped;

            return "application/octet-stream";
        }
    }
}
