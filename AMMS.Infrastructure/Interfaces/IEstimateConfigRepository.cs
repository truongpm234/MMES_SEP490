using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Interfaces
{
    public interface IEstimateConfigRepository
    {
        Task<decimal?> GetNumberAsync(string configGroup, string configKey, CancellationToken ct = default);
    }
}
