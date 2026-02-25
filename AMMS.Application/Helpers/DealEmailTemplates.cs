using AMMS.Infrastructure.Entities;
using System.Globalization;
using System.Text;

namespace AMMS.Application.Helpers
{
    public static class DealEmailTemplates
    {
        private static string VND(decimal v)
            => string.Format(new CultureInfo("vi-VN"), "{0:N0} ₫", v);
        private static string MapProcessCode(string code) => code.Trim().ToUpperInvariant() switch
        {
            "IN" => "In",
            "RALO" => "Ra lô",
            "CAT" => "Cắt",
            "CAN_MANG" => "Cán",
            "CAN" => "Cán",
            "BOI" => "Bồi",
            "PHU" => "Phủ",
            "DUT" => "Dứt",
            "DAN" => "Dán",
            "BE" => "Bế",
            _ => code
        };

        private static string QuoteEmailInner(order_request req, cost_estimate est, quote q, string? orderDetailUrl)
        {
            var delivery = req.delivery_date?.ToString("dd/MM/yyyy") ?? "N/A";
            var requestDateText = req.order_request_date?.ToString("dd/MM/yyyy HH:mm") ?? "N/A";
            var productName = req.product_name ?? "";
            var quantity = req.quantity ?? 0;

            var paperName = string.IsNullOrWhiteSpace(est.paper_name) ? "N/A" : est.paper_name;
            var coatingType = string.IsNullOrWhiteSpace(est.coating_type) ? "N/A" : est.coating_type;
            var waveType = string.IsNullOrWhiteSpace(est.wave_type) ? "N/A" : est.wave_type;

            var designType = req.is_send_design == true ? "Tự gửi file thiết kế" : "Sử dụng bản thiết kế của doanh nghiệp";

            var materialCost = est.paper_cost + est.ink_cost + est.coating_glue_cost + est.mounting_glue_cost + est.lamination_cost;
            var laborCost = est.process_costs != null
                ? est.process_costs.Where(p => p.estimate_id == est.estimate_id).Sum(p => p.total_cost)
                : 0m;

            var otherFees = est.design_cost;
            var rushAmount = est.rush_amount;
            var subtotal = est.subtotal;
            var finalTotal = est.final_total_cost;
            var discountPercent = est.discount_percent;
            var discountAmount = est.discount_amount;
            var deposit = est.deposit_amount;

            var expiredAt = q.created_at.AddHours(24);
            bool isCustomerCopy = !string.IsNullOrEmpty(orderDetailUrl);

            string FormatVND(decimal? amount) => string.Format("{0:N0} đ", amount ?? 0).Replace(",", ".");

            var font = "font-family:'Segoe UI', Tahoma, Verdana, Arial, Helvetica, sans-serif;";
            var tableFixed = "width:100%;border-collapse:collapse;table-layout:fixed;";
            var tdLabel = "width:130px;white-space:nowrap;color:#64748b;font-size:13px;padding:7px 0;border-bottom:1px solid #eef2f7;vertical-align:top;";
            var tdValue = "color:#0f172a;font-size:13px;font-weight:700;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;vertical-align:top;word-break:break-word;";
            var tdValueLink = "color:#2563eb;font-size:13px;font-weight:800;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;vertical-align:top;word-break:break-word;";
            var card = "background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden;box-shadow:0 6px 20px rgba(15,23,42,0.08);";
            var header = "background:linear-gradient(90deg,#1e4f86 0%,#1d3f73 100%);padding:18px 20px;";
            var badge = "display:inline-block;background:rgba(255,255,255,0.18);color:#fff;padding:6px 10px;border-radius:6px;font-size:12px;font-weight:700;";
            var h1 = "color:#ffffff;font-size:18px;font-weight:800;margin:4px 0 0 0;letter-spacing:0.2px;";
            var smallTop = "color:#cfe6ff;font-size:11px;font-weight:800;letter-spacing:1.2px;text-transform:uppercase;";
            var bodyPad = "padding:18px 18px 16px 18px;";
            var sectionTitleBlue = "font-size:13px;font-weight:800;text-transform:uppercase;letter-spacing:0.6px;color:#2563eb;border-bottom:2px solid #bfdbfe;padding-bottom:6px;margin:0 0 10px 0;";
            var sectionTitleOrange = "font-size:13px;font-weight:800;text-transform:uppercase;letter-spacing:0.6px;color:#d97706;border-bottom:2px solid #fde68a;padding-bottom:6px;margin:0 0 10px 0;";
            var sectionTitleGreen = "font-size:13px;font-weight:800;text-transform:uppercase;letter-spacing:0.6px;color:#16a34a;border-bottom:2px solid #bbf7d0;padding-bottom:6px;margin:0 0 10px 0;";

            var label = "color:#64748b;font-size:13px;padding:7px 0;border-bottom:1px solid #eef2f7;";
            var value = "color:#0f172a;font-size:13px;font-weight:700;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;";
            var valueLink = "color:#2563eb;font-size:13px;font-weight:800;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;";

            var boxCost = "background:#fff7ed;border:1px solid #fed7aa;border-radius:10px;padding:12px;";
            var line = "padding:6px 0;font-size:13px;color:#334155;";
            var money = "padding:6px 0;font-size:13px;color:#0f172a;font-weight:800;text-align:right;";
            var moneyRed = "padding:6px 0;font-size:13px;color:#dc2626;font-weight:900;text-align:right;";

            var totalLabel = "color:#64748b;font-size:13px;padding:6px 0;";
            var totalValue = "color:#0f172a;font-size:13px;font-weight:800;text-align:right;padding:6px 0;";
            var finalRowLeft = "font-weight:900;color:#0f172a;font-size:14px;padding-top:10px;";
            var finalRowRight = "font-weight:900;color:#1d4ed8;font-size:18px;text-align:right;padding-top:10px;";
            var vatNote = "text-align:right;font-size:11px;color:#ef4444;padding-top:4px;";

            var depositBox = "margin-top:12px;background:#ecfdf5;border:1px solid #86efac;border-radius:10px;padding:12px;";
            var depositLeft = "color:#166534;font-weight:900;font-size:13px;";
            var depositRight = "color:#166534;font-weight:900;font-size:16px;text-align:right;";

            var btn = "background:#2563eb;color:#ffffff;display:inline-block;padding:12px 18px;border-radius:8px;text-decoration:none;font-size:13px;font-weight:900;box-shadow:0 8px 14px rgba(37,99,235,0.25);";
            var note = "margin-top:14px;font-size:12px;color:#64748b;font-style:italic;line-height:1.5;border-top:1px solid #e2e8f0;padding-top:12px;";
            var warnBox = "background:#f1f5f9;border:1px dashed #94a3b8;border-radius:10px;padding:10px 12px;display:inline-block;";
            var warnText = "color:#475569;font-weight:800;font-size:12px;";

            var expiryNoteHtml = $@"
<div style='{note}'>
  (*) Báo giá có hiệu lực đến <b>{expiredAt:dd/MM/yyyy HH:mm}</b>. Sau thời gian này, đơn giá và chi phí có thể thay đổi.
</div>";

            string actionBlock;
            if (isCustomerCopy)
            {
                actionBlock = $@"
<div style='text-align:center;margin-top:14px;'>
  <a href='{orderDetailUrl}' style='{btn}'>XEM CHI TIẾT &amp; THANH TOÁN</a>
</div>
{expiryNoteHtml}";
            }
            else
            {
                actionBlock = $@"
<div style='text-align:center;margin-top:14px;'>
  <div style='{warnBox}'><span style='{warnText}'>⚠ Email copy cho tư vấn viên.</span></div>
</div>
{expiryNoteHtml}";
            }

            return $@"
<div style='{font}{card}'>
  <div style='{header}'>
    <table width='100%' cellpadding='0' cellspacing='0' border='0'>
      <tr>
        <td style='vertical-align:top;'>
          <div style='{smallTop}'>MES SYSTEM</div>
          <div style='{h1}'>BÁO GIÁ (E{est.estimate_id})</div>
        </td>
        <td align='right' style='vertical-align:top;'>
          <span style='{badge}'>AM{req.order_request_id:D6}</span>
        </td>
      </tr>
    </table>
  </div>

  <div style='{bodyPad}'>
    <table width='100%' cellpadding='0' cellspacing='0' border='0'>
      <tr>
        <td width='50%' style='vertical-align:top;padding-right:10px;'>
          <div style='{sectionTitleBlue}'>Thông tin đơn hàng</div>
          <table style='{tableFixed}' cellpadding='0' cellspacing='0' border='0'>
  <tr><td style='{tdLabel}'>Ngày yêu cầu</td><td style='{tdValue}'>{requestDateText}</td></tr>
  <tr><td style='{tdLabel}'>Người yêu cầu</td><td style='{tdValue};text-transform:uppercase'>{req.customer_name}</td></tr>
  <tr><td style='{tdLabel}'>Số điện thoại</td><td style='{tdValue}'>{req.customer_phone}</td></tr>
  <tr><td style='{tdLabel}'>Email</td><td style='{tdValueLink}'>{req.customer_email}</td></tr>
</table>
        </td>
        <td width='50%' style='vertical-align:top;padding-left:10px;'>
          <div style='{sectionTitleOrange}'>Bảng kê chi phí</div>
          <div style='{boxCost}'>
            <table width='100%' cellpadding='0' cellspacing='0' border='0'>
              <tr><td style='{line}'>Nguyên vật liệu</td><td style='{money}'>{FormatVND(materialCost)}</td></tr>
              <tr><td style='{line}'>Chi phí nhân công</td><td style='{money}'>{FormatVND(laborCost)}</td></tr>
              <tr><td style='{line}'>Chi phí khác</td><td style='{money}'>{FormatVND(otherFees)}</td></tr>
              <tr><td style='{line}'>Phụ thu giao gấp</td><td style='{money}'>{FormatVND(rushAmount)}</td></tr>
            </table>
          </div>
        </td>
      </tr>
    </table>

    <div style='height:14px;'></div>

    <table width='100%' cellpadding='0' cellspacing='0' border='0'>
      <tr>
        <td width='50%' style='vertical-align:top;padding-right:10px;'>
          <div style='{sectionTitleBlue}'>Chi tiết sản phẩm</div>
          <table style='{tableFixed}' cellpadding='0' cellspacing='0' border='0'>
  <tr><td style='{tdLabel}'>Sản phẩm</td><td style='{tdValue}'>{productName}</td></tr>
  <tr><td style='{tdLabel}'>Số lượng</td><td style='{tdValue}'>{quantity:N0}</td></tr>
  <tr><td style='{tdLabel}'>Loại giấy</td><td style='{tdValue}'>{paperName}</td></tr>
  <tr><td style='{tdLabel}'>Phủ</td><td style='{tdValue}'>{coatingType}</td></tr>
  <tr><td style='{tdLabel}'>Sóng</td><td style='{tdValue}'>{waveType}</td></tr>
  <tr><td style='{tdLabel}'>Thiết kế</td><td style='{tdValue}'>{designType}</td></tr>
  <tr><td style='{tdLabel}'>Giao dự kiến</td><td style='{tdValue}'>{delivery}</td></tr>
</table>
        </td>

        <td width='50%' style='vertical-align:top;padding-left:10px;'>
          <div style='{sectionTitleGreen}'>Tổng thanh toán</div>
          <table width='100%' cellpadding='0' cellspacing='0' border='0'>
            <tr><td style='{totalLabel}'>Tạm tính</td><td style='{totalValue}'>{FormatVND(subtotal)}</td></tr>
            <tr>
              <td style='{totalLabel};border-bottom:1px dashed #cbd5e1;'>Giảm giá ({discountPercent:0.#}%)</td>
              <td style='{moneyRed};border-bottom:1px dashed #cbd5e1;'>- {FormatVND(discountAmount)}</td>
            </tr>
            <tr><td style='{finalRowLeft}'>THÀNH TIỀN</td><td style='{finalRowRight}'>{FormatVND(finalTotal)}</td></tr>
            <tr><td colspan='2' style='{vatNote}'>(Đã bao gồm VAT)</td></tr>
          </table>

          <div style='{depositBox}'>
            <table width='100%' cellpadding='0' cellspacing='0' border='0'>
              <tr>
                <td style='{depositLeft}'>Cần cọc:</td>
                <td style='{depositRight}'>{FormatVND(deposit)}</td>
              </tr>
            </table>
          </div>
        </td>
      </tr>
    </table>

    {actionBlock}
  </div>
</div>";
        }
        public static string QuoteEmail(order_request req, cost_estimate est, quote q, string orderDetailUrl)
        {
            var inner = QuoteEmailInner(req, est, q, orderDetailUrl);

            return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='background-color:#f7fafc;padding:30px 0;font-family:Arial,Helvetica,sans-serif;'>
  <div style='max-width:700px;margin:0 auto;'>
    {inner}
    <div style='background-color:#edf2f7;padding:15px;text-align:center;font-size:12px;color:#718096;'>
      Email này được gửi tự động từ hệ thống MES.
    </div>
  </div>
</body>
</html>";
        }

        public static string QuoteEmailCompare(
            order_request req,
            List<(cost_estimate est, quote q, string? checkoutUrl)> pairs)
        {
            if (pairs == null || pairs.Count == 0)
                return QuoteEmail(req, new cost_estimate(), new quote { created_at = AppTime.NowVnUnspecified() }, "");

            // tối đa 2 theo yêu cầu
            var left = pairs[0];
            var right = pairs.Count > 1 ? pairs[1] : ((cost_estimate est, quote q, string? checkoutUrl)?)null;

            var leftHtml = QuoteEmailInner(req, left.est, left.q, left.checkoutUrl);
            var rightHtml = right.HasValue ? QuoteEmailInner(req, right.Value.est, right.Value.q, right.Value.checkoutUrl) : "";

            return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='background-color:#f7fafc;padding:30px 0;font-family:Arial,Helvetica,sans-serif;'>
  <div style='max-width:1100px;margin:0 auto;'>

    <div style='margin-bottom:18px;text-align:center;color:#334155;font-weight:800;font-size:18px;'>
      BÁO GIÁ ĐƠN HÀNG AM{req.order_request_id:D6}
    </div>

    <table width='100%' cellpadding='0' cellspacing='0' border='0'>
  <tr>
    <td width='50%' valign='top' style='padding:0 10px 0 0;'>
      {leftHtml}
    </td>
    <td width='50%' valign='top' style='padding:0 0 0 10px;'>
      {(right.HasValue ? rightHtml : "<div style='font-family:Arial,Helvetica,sans-serif;color:#64748b;font-size:13px;padding:18px;background:#fff;border:1px dashed #cbd5e1;border-radius:12px;'>Chỉ có 1 báo giá active.</div>")}
    </td>
  </tr>
</table>
    <div style='background-color:#edf2f7;padding:15px;text-align:center;font-size:12px;color:#718096;margin-top:16px;'>
      Email này được gửi tự động từ hệ thống MES.
    </div>

  </div>
</body>
</html>";
        }
        public static string AcceptCustomerEmail(order_request req, order order, cost_estimate est, string trackingUrl)
        {
            return $@"
<div style='font-family:Arial,Helvetica,sans-serif;max-width:720px;margin:24px auto;line-height:1.6'>
  <h2 style='margin-top:0;'>ĐƠN HÀNG ĐÃ ĐƯỢC PHÊ DUYỆT</h2>

  <p>Cảm ơn bạn đã xác nhận báo giá.</p>

  <table style='border-collapse:collapse;width:100%;margin:12px 0 16px 0;'>
    <tr>
      <td style='border:1px solid transparent;padding:4px 8px;width:30%;'><b>Mã đơn hàng</b></td>
      <td style='border:1px solid transparent;padding:4px 8px;'><span style='color:blue'>{order.code}</span></td>
    </tr>
    <tr>
      <td style='border:1px solid transparent;padding:4px 8px;'><b>Sản phẩm</b></td>
      <td style='border:1px solid transparent;padding:4px 8px;'>{req.product_name}</td>
    </tr>
    <tr>
      <td style='border:1px solid transparent;padding:4px 8px;'><b>Số lượng</b></td>
      <td style='border:1px solid transparent;padding:4px 8px;'>{req.quantity}</td>
    </tr>
    <tr>
      <td style='border:1px solid transparent;padding:4px 8px;'><b>Giá trị đơn hàng</b></td>
      <td style='border:1px solid transparent;padding:4px 8px;'>{VND(est.final_total_cost)}</td>
    </tr>
  </table>

  <hr style='border:none;border-top:1px solid #e5e7eb;margin:16px 0;' />

  <p>
    Bạn có thể theo dõi tiến độ sản xuất tại:
    <br/>
    <a href='{trackingUrl}'>{trackingUrl}</a>
  </p>

  <p>
    Vui lòng lưu lại <b>mã đơn hàng</b> để tra cứu sau này.
  </p>

  <p>MES trân trọng!</p>
</div>";
        }

        public static string AcceptConsultantEmail(order_request req, order order)
        {
            return $@"
<div style='font-family:Arial,Helvetica,sans-serif;max-width:720px;margin:24px auto;line-height:1.6'>
  <h3 style='margin-top:0;'>KHÁCH HÀNG ĐÃ ĐỒNG Ý BÁO GIÁ</h3>

  <table style='border-collapse:collapse;width:100%;margin-top:8px;'>
    <tr>
      <td style='border:1px solid transparent;padding:4px 8px;width:30%;'><b>Request ID</b></td>
      <td style='border:1px solid transparent;padding:4px 8px;'>{req.order_request_id}</td>
    </tr>
    <tr>
      <td style='border:1px solid transparent;padding:4px 8px;'><b>Order Code</b></td>
      <td style='border:1px solid transparent;padding:4px 8px;'><b>{order.code}</b></td>
    </tr>
    <tr>
      <td style='border:1px solid transparent;padding:4px 8px;'><b>Sản phẩm</b></td>
      <td style='border:1px solid transparent;padding:4px 8px;'>{req.product_name}</td>
    </tr>
    <tr>
      <td style='border:1px solid transparent;padding:4px 8px;'><b>Số lượng</b></td>
      <td style='border:1px solid transparent;padding:4px 8px;'>{req.quantity}</td>
    </tr>
  </table>
</div>";
        }
    }
}
