using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Socket
{
    public record TaskLogCreatedEvent(
    int task_id,
    int prod_id,
    string? action_type,
    int? qty_good,
    DateTime log_time
);

    public record TaskUpdatedEvent(
        int task_id,
        int prod_id,
        string? status,
        DateTime? start_time,
        DateTime? end_time
    );

}
