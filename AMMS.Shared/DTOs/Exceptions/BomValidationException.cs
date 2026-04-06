using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Exceptions
{
    public sealed class BomMissingMaterialItem
    {
        public int bom_id { get; init; }
        public int order_item_id { get; init; }
        public int? source_estimate_id { get; init; }
        public string? material_code { get; init; }
        public string? material_name { get; init; }
        public string? unit { get; init; }
        public decimal qty_total { get; init; }
    }

    public sealed class BomValidationException : InvalidOperationException
    {
        public IReadOnlyList<BomMissingMaterialItem> Items { get; }

        public BomValidationException(IReadOnlyList<BomMissingMaterialItem> items)
            : base("BOM contains unmapped material lines.")
        {
            Items = items;
        }
    }
}
