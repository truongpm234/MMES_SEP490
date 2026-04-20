using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface IPdfDigitalSignatureValidator
    {
        Task<PdfDigitalSignatureValidationResult> ValidateAsync(
            byte[] pdfBytes,
            CancellationToken ct = default);
    }

    public class PdfDigitalSignatureValidationResult
    {
        public bool has_signature { get; set; }
        public bool is_valid { get; set; }
        public int signature_count { get; set; }
        public string? detail { get; set; }
    }
}
