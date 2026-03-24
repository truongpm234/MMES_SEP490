using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Estimates;

public class SignedPreviewPagesResponse
{
    public int request_id { get; set; }
    public int estimate_id { get; set; }
    public int page_count { get; set; }
    public List<string> preview_urls { get; set; } = new();
}
