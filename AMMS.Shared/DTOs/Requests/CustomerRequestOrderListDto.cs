using AMMS.Shared.DTOs.Common;
using AMMS.Shared.DTOs.Orders;

namespace AMMS.Shared.DTOs.Requests
{
    public class CustomerRequestOrderListDto
    {
        public int user_id { get; set; }
        public string phone_number { get; set; } = string.Empty;

        public PagedResultLite<RequestSortedDto> requests { get; set; } = new();
        public PagedResultLite<OrderListDto> orders { get; set; } = new();
    }
}