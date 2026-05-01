using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Entities
{
    [Table("product_receipts", Schema = "AMMS_DB")]
    public partial class product_receipt
    {
        public int receipt_id { get; set; }
        public string code { get; set; } = null!;
        public DateTime created_at { get; set; }
        public int? created_by { get; set; }
        public string? note { get; set; }

        public virtual user? created_byNavigation { get; set; }
        public virtual ICollection<product_receipt_item> product_receipt_items { get; set; } = new List<product_receipt_item>();
    }
}
