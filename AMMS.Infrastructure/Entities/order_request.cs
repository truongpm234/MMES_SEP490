using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities;

[Table("order_request", Schema = "AMMS_DB")]
public partial class order_request
{
    public int order_request_id { get; set; }

    public string? customer_name { get; set; }

    public string? customer_phone { get; set; }

    public string? customer_email { get; set; }

    public DateTime? delivery_date { get; set; }

    public string? product_name { get; set; }

    public int? quantity { get; set; }

    public string? description { get; set; }

    public string? design_file_path { get; set; }

    public string? print_ready_file { get; set; }

    public DateTime? order_request_date { get; set; }

    public string? detail_address { get; set; }

    public string? process_status { get; set; }

    public string? product_type { get; set; }

    public int? number_of_plates { get; set; }

    public int? order_id { get; set; }

    public int? quote_id { get; set; }

    public int? product_length_mm { get; set; }

    public int? product_width_mm { get; set; }

    public int? product_height_mm { get; set; }

    public int? glue_tab_mm { get; set; }

    public int? bleed_mm { get; set; }

    public bool? is_one_side_box { get; set; }

    public int? print_width_mm { get; set; }

    public int? print_length_mm { get; set; }

    public bool? is_send_design { get; set; }

    public string? note { get; set; }

    public string? reason { get; set; }

    public int? accepted_estimate_id { get; set; }

    public string? consultant_note { get; set; }

    [Column("verified_at")]
    public DateTime? verified_at { get; set; }

    [Column("quote_expire_at")]
    public DateTime? quote_expires_at { get; set; }

    public string? message_to_customer { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal? preliminary_estimated_price { get; set; }

    public int? assigned_consultant { get; set; }

    public DateTime? assigned_at { get; set; }

    public string? delivery_note { get; set; }

    public int? actual_consultant_user_id { get; set; }

    public string? assign_name { get; set; }

    public string? delivery_date_change_reason { get; set; }

    [ForeignKey(nameof(assigned_consultant))]
    public virtual user? assigned_consultants { get; set; }

    public DateTime? estimate_finish_date { get; set; }

    public virtual quote? quote { get; set; }

    public virtual order? order { get; set; }

    public virtual ICollection<payment> payments { get; set; } = new List<payment>();

}
