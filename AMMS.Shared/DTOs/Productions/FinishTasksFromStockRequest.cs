using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class FinishTasksFromStockRequest
    {
        public List<int> task_ids { get; set; } = new();
    }
}
