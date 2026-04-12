using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities;

[Table("orders", Schema = "AMMS_DB")]
public partial class order
{
    public int order_id { get; set; }

    public string code { get; set; } = null!;

    public int? quote_id { get; set; }

    public DateTime? order_date { get; set; }

    public DateTime? delivery_date { get; set; }

    public decimal? total_amount { get; set; }

    public string? status { get; set; }

    public bool? is_enough { get; set; }

    public bool? is_buy { get; set; }

    public string? payment_status { get; set; }

    public int? production_id { get; set; }

    public bool layout_confirmed { get; set; } = false;

    public bool is_production_ready { get; set; } = false;

    public DateTime? confirmed_delivery_at { get; set; }

    public virtual production? production { get; set; }

    public virtual ICollection<delivery> deliveries { get; set; } = new List<delivery>();

    public virtual ICollection<order_item> order_items { get; set; } = new List<order_item>();

    public virtual ICollection<production> productions { get; set; } = new List<production>();

    public virtual quote? quote { get; set; }
}
