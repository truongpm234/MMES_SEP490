using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities
{
    [Table("payments", Schema = "AMMS_DB")]
    public partial class payment
    {
        public int payment_id { get; set; }

        public int order_request_id { get; set; }

        public string provider { get; set; } = "PAYOS";

        public long order_code { get; set; }


        [Column(TypeName = "numeric(18,2)")]
        public decimal amount { get; set; }

        public string currency { get; set; } = "VND";

        public string status { get; set; } = "PENDING";

        public DateTime? paid_at { get; set; }

        public string? payos_payment_link_id { get; set; }

        public string? payos_transaction_id { get; set; }

        public int? estimate_id { get; set; }

        [Column(TypeName = "jsonb")]
        public string? payos_raw { get; set; }

        public DateTime created_at { get; set; }

        public DateTime updated_at { get; set; }

        public int? quote_id { get; set; }

        public virtual order_request order_request { get; set; } = null!;
    }
}
