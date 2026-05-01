using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities
{
    [Table("products", Schema = "AMMS_DB")]
    public partial class product
    {
        public int product_id { get; set; }

        public int product_type_id { get; set; }

        public string code { get; set; } = null!;

        public string name { get; set; } = null!;

        public string? description { get; set; }

        public bool is_active { get; set; } = true;

        public DateTime created_at { get; set; }

        public DateTime? updated_at { get; set; }

        public int stock_qty { get; set; }

        [ForeignKey(nameof(product_type_id))]
        public virtual product_type product_type { get; set; } = null!;

        public virtual ICollection<product_receipt_item> product_receipt_items { get; set; } = new List<product_receipt_item>();
    }
}

