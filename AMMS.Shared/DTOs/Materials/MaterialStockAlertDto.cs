using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Materials
{
    public class MaterialStockAlertDto
    {
        public int MaterialId { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Unit { get; set; }
        public string? Type { get; set; }
        public string? MaterialClass { get; set; }

        public decimal StockQty { get; set; }
        public decimal MinStockQty { get; set; }
        public decimal GapToMinStock { get; set; }

        public bool IsLowStock { get; set; }
        public bool IsNearMinStock { get; set; }

        public string WarningLevel { get; set; } = "NORMAL";
        public string? WarningMessage { get; set; }
    }
}
