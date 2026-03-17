using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.DTOs.Background
{
    public sealed record EmailQueueItem(
        string To,
        string Subject,
        string Html
    );
}
