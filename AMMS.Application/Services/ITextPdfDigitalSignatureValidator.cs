using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AMMS.Application.Interfaces;
using iText.Kernel.Pdf;
using iText.Signatures;

namespace AMMS.Application.Services
{
    public class ITextPdfDigitalSignatureValidator : IPdfDigitalSignatureValidator
    {
        public Task<PdfDigitalSignatureValidationResult> ValidateAsync(
            byte[] pdfBytes,
            CancellationToken ct = default)
        {
            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            var util = new SignatureUtil(pdfDoc);
            var names = util.GetSignatureNames();

            if (names == null || names.Count == 0)
            {
                return Task.FromResult(new PdfDigitalSignatureValidationResult
                {
                    has_signature = false,
                    is_valid = false,
                    signature_count = 0,
                    detail = "No PDF digital signature found."
                });
            }

            var validCount = 0;

            foreach (var name in names)
            {
                var pkcs7 = util.ReadSignatureData(name);
                var integrityOk = pkcs7.VerifySignatureIntegrityAndAuthenticity();
                var coversWholeDocument = util.SignatureCoversWholeDocument(name);

                if (integrityOk && coversWholeDocument)
                    validCount++;
            }

            return Task.FromResult(new PdfDigitalSignatureValidationResult
            {
                has_signature = names.Count > 0,
                is_valid = validCount > 0,
                signature_count = names.Count,
                detail = validCount > 0
                    ? "Valid PDF digital signature detected."
                    : "Signature exists but validation failed."
            });
        }
    }
}
