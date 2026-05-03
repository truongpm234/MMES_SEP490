using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class FinishTasksFromStockResponse
    {
        public List<int> finished_task_ids { get; set; } = new();
        public List<int> already_finished_task_ids { get; set; } = new();
        public List<int> not_found_task_ids { get; set; } = new();
    }
}
