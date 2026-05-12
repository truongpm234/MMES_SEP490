using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities;

[Table("prod_orders", Schema = "AMMS_DB")]
public class prod_order
{
    public int id { get; set; }

    public int prod_id { get; set; }

    public int order_id { get; set; }

    public int? single_prod_id { get; set; }

    public int qty { get; set; }

    public int? product_type_id { get; set; }

    public string? product_process { get; set; }

    public string status { get; set; } = "Active";

    public DateTime? created_at { get; set; }

    public virtual production production { get; set; } = null!;

    public virtual order order { get; set; } = null!;

    public virtual production? single_production { get; set; }
}