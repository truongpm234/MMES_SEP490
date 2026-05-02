using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities;

[Table("product_types", Schema = "AMMS_DB")]
public partial class product_type
{
    public int product_type_id { get; set; }

    public string code { get; set; } = null!;

    public string name { get; set; } = null!;

    public string? description { get; set; }

    public bool? is_active { get; set; }

    public string? packaging_standard { get; set; }

    public virtual ICollection<order_item> order_items { get; set; } = new List<order_item>();

    public virtual ICollection<product_type_process> product_type_processes { get; set; } = new List<product_type_process>();

    public virtual ICollection<production> productions { get; set; } = new List<production>();

    public virtual ICollection<product_template> product_type_design_profiles { get; set; } = new List<product_template>();

    public virtual ICollection<product> products { get; set; } = new List<product>();

    public virtual ICollection<sub_product> sub_products { get; set; } = new List<sub_product>();

}
