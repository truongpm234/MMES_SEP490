using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMMS.Infrastructure.Entities
{
    [Table("production_calendar", Schema = "AMMS_DB")]
    public partial class production_calendar
    {
        [Key]
        public DateTime calendar_date { get; set; }

        [StringLength(255)]
        public string? holiday_name { get; set; }

        [StringLength(50)]
        public string holiday_type { get; set; } = null!;

        public bool is_non_working_day { get; set; }

        public bool is_manual_override { get; set; }

        public string? note { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime created_at { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime updated_at { get; set; }
    }
}