namespace AMMS.Shared.DTOs.Estimates
{
    namespace AMMS.Shared.DTOs.Estimates
    {
        public class PaperEstimateResponse
        {
            public string paper_code { get; set; } = null!;

            public int sheet_width_mm { get; set; }

            public int sheet_length_mm { get; set; }

            public int print_width_mm { get; set; }

            public int print_length_mm { get; set; }

            public int n_up { get; set; }

            public int quantity { get; set; }

            public int sheets_base { get; set; }

            public int waste_printing { get; set; }

            public int waste_die_cutting { get; set; }

            public int waste_mounting { get; set; }

            public int waste_coating { get; set; }

            public int waste_lamination { get; set; }

            public int waste_gluing { get; set; }

            public int total_waste { get; set; }

            public int sheets_with_waste { get; set; }

            public decimal waste_percent { get; set; }

            public string? warning_message { get; set; }
        }
    }
}
