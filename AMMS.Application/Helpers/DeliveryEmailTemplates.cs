using System.Globalization;
using System.Net;
using AMMS.Infrastructure.Entities;

namespace AMMS.Application.Helpers
{
    public static class DeliveryEmailTemplates
    {
        private const string FontFamily = "\"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif";

        private static string VND(decimal v)
            => string.Format(new CultureInfo("vi-VN"), "{0:N0} ₫", v);

        private static string Safe(string? s)
            => WebUtility.HtmlEncode((s ?? "").Trim());

        private static string PlainReceiveUrlBlock(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            var encodedUrl = WebUtility.HtmlEncode(url);

            var safeUrl = encodedUrl
                .Replace("://", "<span>://</span>")
                .Replace(".", "<span>.</span>");

            return $@"
<div style='margin:0 0 24px 0;max-width:100%;background:#f0f9ff;border:2px solid #bae6fd;border-radius:12px;padding:24px 20px;text-align:left;box-shadow:0 4px 15px rgba(0,0,0,0.04);'>
  <p style='font-size:22px;color:#0369a1;font-weight:900;margin:0 0 12px 0;line-height:1.4;'>
    📌 Đường dẫn xác nhận đã nhận hàng
  </p>

  <p style='font-size:15px;color:#334155;line-height:1.6;margin:0 0 16px 0;'>
    Đơn hàng của Quý khách đã được bàn giao cho đơn vị vận chuyển.
    Khi đã nhận được hàng, vui lòng <b>copy đường dẫn bên dưới</b> và dán vào trình duyệt để xác nhận đã nhận hàng.
  </p>

  <div style='font-size:20px;color:#0284c7;word-break:break-all;line-height:1.6;margin:0;font-family:""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;font-weight:900;background:#ffffff;border:2px dashed #7dd3fc;border-radius:8px;padding:16px 20px;user-select:all;-webkit-user-select:all;text-decoration:none;cursor:text;text-align:center;'>
    {safeUrl}
  </div>
</div>";
        }

        public static string BuildDeliveryHandoverEmail(
            order_request req,
            order ord,
            production? prod,
            cost_estimate? est,
            string confirmReceiveUrl)
        {
            var productName = Safe(req.product_name);
            var customerName = Safe(req.customer_name);
            var phone = Safe(req.customer_phone);
            var email = Safe(req.customer_email);
            var address = Safe(req.detail_address);
            var orderCode = Safe(ord.code);
            var quantity = req.quantity ?? 0;
            var deliveryDate = req.delivery_date?.ToString("dd/MM/yyyy") ?? "N/A";
            var handedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            var finalTotal = est?.final_total_cost ?? 0m;

            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
</head>
<body style='margin:0;background:#f8fafc;padding:30px 0;font-family:{FontFamily};'>
  <div style='max-width:760px;margin:0 auto;padding:0 12px;'>

    <div style='background:linear-gradient(135deg,#2563eb 0%,#1e3a8a 100%);padding:24px 26px;border-radius:18px 18px 0 0;color:#ffffff;'>
      <div style='font-size:11px;font-weight:800;letter-spacing:1px;text-transform:uppercase;opacity:0.9;'>MES DELIVERY NOTICE</div>
      <div style='font-size:24px;font-weight:900;margin-top:8px;'>Đơn hàng đã được bàn giao cho đơn vị vận chuyển</div>
      <div style='font-size:13px;margin-top:6px;color:#dbeafe;'>
        Quý khách vui lòng theo dõi quá trình giao hàng và xác nhận sau khi đã nhận đủ hàng.
      </div>
    </div>

    <div style='background:#ffffff;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 18px 18px;padding:24px 24px 20px;box-shadow:0 10px 28px rgba(15,23,42,0.06);'>

      <p style='margin:0 0 14px 0;font-size:14px;color:#334155;line-height:1.8;'>
        Kính gửi <b>{customerName}</b>,
      </p>

      <p style='margin:0 0 14px 0;font-size:14px;color:#334155;line-height:1.8;'>
        Chúng tôi xin thông báo rằng đơn hàng của Quý khách đã được <b>bàn giao cho đơn vị vận chuyển</b>.
      </p>

      <p style='margin:0 0 16px 0;font-size:14px;color:#334155;line-height:1.8;'>
        Sau khi nhận được hàng, Quý khách vui lòng xác nhận để hệ thống hoàn tất trạng thái đơn hàng.
      </p>

      <div style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:14px;padding:16px 18px;margin:14px 0 18px 0;'>
        <div style='font-size:13px;font-weight:800;color:#334155;margin-bottom:10px;text-transform:uppercase;'>Thông tin đơn hàng</div>

        <table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;'>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#64748b;width:38%;'>Mã order</td>
            <td style='padding:6px 0;font-size:13px;color:#0f172a;font-weight:700;'>{orderCode}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#64748b;'>Mã request</td>
            <td style='padding:6px 0;font-size:13px;color:#0f172a;font-weight:700;'>AM{req.order_request_id:D6}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#64748b;'>Sản phẩm</td>
            <td style='padding:6px 0;font-size:13px;color:#0f172a;font-weight:700;'>{productName}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#64748b;'>Số lượng</td>
            <td style='padding:6px 0;font-size:13px;color:#0f172a;font-weight:700;'>{quantity:N0}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#64748b;'>Ngày giao dự kiến</td>
            <td style='padding:6px 0;font-size:13px;color:#0f172a;font-weight:700;'>{deliveryDate}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#64748b;'>Thời điểm bàn giao vận chuyển</td>
            <td style='padding:6px 0;font-size:13px;color:#0f172a;font-weight:700;'>{handedAt}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#64748b;'>Trạng thái hiện tại</td>
            <td style='padding:6px 0;font-size:13px;color:#0f172a;font-weight:700;'>Delivery</td>
          </tr>
        </table>
      </div>

      <div style='background:linear-gradient(135deg,#fff7ed 0%,#fffbeb 100%);border:1px solid #fed7aa;border-radius:14px;padding:16px 18px;margin:0 0 18px 0;'>
        <div style='font-size:13px;font-weight:800;color:#9a3412;margin-bottom:10px;text-transform:uppercase;'>Thông tin khách nhận</div>

        <table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;'>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;width:38%;'>Khách hàng</td>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;font-weight:700;'>{customerName}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;'>Số điện thoại</td>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;font-weight:700;'>{phone}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;'>Email</td>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;font-weight:700;'>{email}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;'>Địa chỉ giao hàng</td>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;font-weight:700;'>{address}</td>
          </tr>
          <tr>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;'>Tổng giá trị đơn</td>
            <td style='padding:6px 0;font-size:13px;color:#7c2d12;font-weight:700;'>{VND(finalTotal)}</td>
          </tr>
        </table>
      </div>

      {PlainReceiveUrlBlock(confirmReceiveUrl)}

      <div style='margin-top:12px;background:#f8fafc;border:1px solid #cbd5e1;border-radius:12px;padding:14px 16px;'>
        <p style='margin:0 0 8px 0;font-size:13px;color:#334155;line-height:1.7;'>
          Sau khi Quý khách xác nhận đã nhận hàng, hệ thống sẽ chuyển đơn hàng sang trạng thái hoàn tất.
        </p>
        <p style='margin:0;font-size:12px;color:#64748b;line-height:1.7;'>
          Nếu có vấn đề về số lượng, chất lượng hoặc giao hàng, vui lòng liên hệ ngay với bộ phận hỗ trợ của chúng tôi.
        </p>
      </div>

      <p style='margin:18px 0 0 0;font-size:13px;color:#475569;line-height:1.7;'>
        Xin chân thành cảm ơn Quý khách đã đồng hành cùng doanh nghiệp.
      </p>
    </div>

    <div style='padding:14px;text-align:center;font-size:12px;color:#64748b;'>
      Email này được gửi tự động từ hệ thống MES.
    </div>
  </div>
</body>
</html>";
        }
    }
}