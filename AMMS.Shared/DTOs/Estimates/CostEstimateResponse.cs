using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates
{
    public class CostEstimateResponse
    {
        public decimal paper_cost { get; set; }

        public int paper_sheets_used { get; set; }

        public decimal paper_unit_price { get; set; }

        public decimal ink_cost { get; set; }

        public decimal ink_weight_kg { get; set; }

        public decimal ink_rate_per_m2 { get; set; }

        public decimal ink_unit_price { get; set; }

        public decimal coating_glue_cost { get; set; }

        public decimal coating_glue_weight_kg { get; set; }

        public decimal coating_glue_rate_per_m2 { get; set; }

        public decimal coating_glue_unit_price { get; set; }

        public string coating_type { get; set; } = "NONE";

        public decimal mounting_glue_cost { get; set; }

        public decimal mounting_glue_weight_kg { get; set; }

        public decimal mounting_glue_rate_per_m2 { get; set; }

        public decimal mounting_glue_unit_price { get; set; }

        public decimal lamination_cost { get; set; }

        public decimal lamination_weight_kg { get; set; }

        public decimal lamination_rate_per_m2 { get; set; }

        public decimal lamination_unit_price { get; set; }

        public decimal material_cost { get; set; }

        //public decimal overhead_percent { get; set; }

        //public decimal overhead_cost { get; set; }

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

        public decimal total_area_m2 { get; set; }

        public decimal design_cost { get; set; }

        public List<MaterialCostDetail> material_cost_details { get; set; } = new();
    }
}
