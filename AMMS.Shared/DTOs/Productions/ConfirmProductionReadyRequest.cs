using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Productions
{
    public class ConfirmProductionReadyRequest
    {
        public bool is_production_ready { get; set; }
    }

    public class ProductionReadyCheckResponse
    {
        public int order_id { get; set; }
        public bool is_production_ready { get; set; }
        public bool has_enough_material { get; set; }
        public bool has_free_machine { get; set; }
    }
}
