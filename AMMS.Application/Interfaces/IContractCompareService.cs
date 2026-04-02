using AMMS.Shared.DTOs.Estimates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Application.Interfaces
{
    public interface IContractCompareService
    {
        Task<CompareContractResponse> CompareAsync(
            int requestId,
            int estimateId,
            string consultantDocxUrl,
            string customerPdfUrl,
            CancellationToken ct = default);

        Task<CompareContractResponse> CompareAsync(
            int requestId,
            int estimateId,
            string consultantDocxUrl,
            byte[] customerPdfBytes,
            CancellationToken ct = default);
    }
}