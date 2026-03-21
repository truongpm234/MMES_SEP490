using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public sealed class ProductionDetailDto
    {
        // Header
        public int prod_id { get; set; }
        public string? production_code { get; set; }
        public string? production_status { get; set; }
        public DateTime? start_date { get; set; }
        public DateTime? end_date { get; set; }

        // Order / Customer
        public int? order_id { get; set; }
        public string? order_code { get; set; }
        public DateTime? delivery_date { get; set; }
        public string customer_name { get; set; } = "";

        // Product
        public string? product_name { get; set; }
        public int quantity { get; set; }

        // Kích thước
        public int? length_mm { get; set; }
        public int? width_mm { get; set; }
        public int? height_mm { get; set; }

        // Timeline stages
        public DateTime? created_at { get; set; }
        public DateTime? planned_start_date { get; set; }
        public DateTime? actual_start_date { get; set; }
        public List<ProductionStageDto> stages { get; set; } = new();
    }
}

