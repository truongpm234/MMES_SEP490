using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities;
[Table("cost_estimate", Schema = "AMMS_DB")]
public partial class cost_estimate
{
    public int estimate_id { get; set; }

    public int order_request_id { get; set; }

    public decimal paper_cost { get; set; }

    public int paper_sheets_used { get; set; }

    public decimal paper_unit_price { get; set; }

    public decimal ink_cost { get; set; }

    public decimal ink_weight_kg { get; set; }

    public decimal ink_rate_per_m2 { get; set; }

    public decimal coating_glue_cost { get; set; }

    public decimal coating_glue_weight_kg { get; set; }

    public decimal coating_glue_rate_per_m2 { get; set; }

    public string? coating_type { get; set; } = "KEO_NUOC";

    public decimal mounting_glue_cost { get; set; }

    public decimal mounting_glue_weight_kg { get; set; }

    public decimal mounting_glue_rate_per_m2 { get; set; }

    public decimal lamination_cost { get; set; }

    public decimal lamination_weight_kg { get; set; }

    public decimal lamination_rate_per_m2 { get; set; }

    public decimal material_cost { get; set; }

    public decimal base_cost { get; set; }

    public bool is_rush { get; set; }

    public decimal rush_percent { get; set; }

    public decimal rush_amount { get; set; }

    public int days_early { get; set; }

    public decimal subtotal { get; set; }

    public decimal discount_percent { get; set; }

    public decimal discount_amount { get; set; }

    public decimal final_total_cost { get; set; }

    public DateTime estimated_finish_date { get; set; }

    public DateTime desired_delivery_date { get; set; }

    public DateTime created_at { get; set; }

    public int sheets_required { get; set; }

    public int sheets_waste { get; set; }

    public int sheets_total { get; set; }

    public int n_up { get; set; }

    public decimal total_area_m2 { get; set; }

    public decimal design_cost { get; set; }

    public string? cost_note { get; set; }

    public bool is_active { get; set; } = true;

    public string? paper_code { get; set; }

    public string? paper_name { get; set; }

    public string? wave_type { get; set; }

    public string? production_processes { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]

    public decimal deposit_amount { get; private set; }

    [Column("previous_estimate_id")]
    public int? previous_estimate_id { get; set; }

    public string? contract_file_path { get; set; }

    public DateTime? contract_uploaded_at { get; set; }

    public virtual cost_estimate? previous_estimate { get; set; }

    public virtual ICollection<cost_estimate> revised_estimates { get; set; } = new List<cost_estimate>();

    public virtual ICollection<cost_estimate_process> process_costs { get; set; } = new List<cost_estimate_process>();

    public virtual order_request order_request { get; set; } = null!;
}