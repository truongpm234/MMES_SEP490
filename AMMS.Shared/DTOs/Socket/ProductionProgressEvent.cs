using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Socket
{
        public record ProductionProgressEvent(
            int prod_id,
            int order_id,
            int total_tasks,
            int finished_tasks,
            int percent
        );
}
