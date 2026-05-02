using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Entities
{
    [Table("product_receipt_items", Schema = "AMMS_DB")]
    public partial class product_receipt_item
    {
        public int id { get; set; }
        public int receipt_id { get; set; }
        public int product_id { get; set; }
        public int qty_received { get; set; }
        public string? note { get; set; }

        public virtual product_receipt receipt { get; set; } = null!;
        public virtual product product { get; set; } = null!;
    }
}
