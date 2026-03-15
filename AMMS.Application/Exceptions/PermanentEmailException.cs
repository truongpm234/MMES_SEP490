using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace AMMS.Application.Exceptions
{
    public class PermanentEmailException : Exception
    {
        public HttpStatusCode? StatusCode { get; }

        public PermanentEmailException(string message, HttpStatusCode? statusCode = null)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
