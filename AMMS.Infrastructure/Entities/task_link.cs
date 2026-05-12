using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities;

[Table("task_links", Schema = "AMMS_DB")]
public class task_link
{
    public int id { get; set; }

    public int group_prod_id { get; set; }

    public int group_task_id { get; set; }

    public int single_prod_id { get; set; }

    public int single_task_id { get; set; }

    public int order_id { get; set; }

    public string? process_code { get; set; }

    public int qty_plan { get; set; }

    public string status { get; set; } = "Waiting";

    public DateTime? created_at { get; set; }
}