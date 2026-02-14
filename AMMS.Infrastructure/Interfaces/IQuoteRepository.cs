using AMMS.Infrastructure.Entities;
using AMMS.Shared.DTOs.Email;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Interfaces
{
    public interface IQuoteRepository
    {
        Task AddAsync(quote entity);
        Task<quote?> GetByIdAsync(int id);
        Task SaveChangesAsync();
        Task<QuoteEmailPreviewResponse> BuildPreviewAsync(int quoteId, CancellationToken ct = default);
    }
}

