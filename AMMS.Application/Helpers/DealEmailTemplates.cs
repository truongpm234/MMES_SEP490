using AMMS.Infrastructure.Entities;
using System.Globalization;

namespace AMMS.Application.Helpers
{
    public static class DealEmailTemplates
    {
        private static string VND(decimal v)
            => string.Format(new CultureInfo("vi-VN"), "{0:N0} ₫", v);

        private static string MapCoatingType(string? coatingType)
        {
            var v = (coatingType ?? "").Trim().ToUpperInvariant();
            return v switch
            {
                "KEO_NUOC" => "Keo nước",
                "KEO_DAU" => "Keo dầu",
                "" => "N/A",
                _ => coatingType ?? "N/A"
            };
        }

        private static string QuoteIntro(order_request req)
        {
            var customerName = string.IsNullOrWhiteSpace(req.customer_name)
                ? "Quý khách"
                : req.customer_name!.Trim();

            return $@"
<div style='background:linear-gradient(180deg,#ffffff 0%,#f8fbff 100%);border:1px solid #dbe7f3;border-radius:16px;padding:22px 24px;margin-bottom:18px;box-shadow:0 10px 28px rgba(15,23,42,0.06);font-family:Roboto, Arial, Helvetica, sans-serif;line-height:1.78;color:#334155;'>
  <div style='font-size:20px;font-weight:800;color:#1e3a8a;margin-bottom:10px;letter-spacing:0.2px;'>
    Báo giá đơn hàng
  </div>

  <p style='margin:0 0 12px 0;font-size:14px;'>
    Kính gửi <b>{customerName}</b>,
  </p>

  <p style='margin:0 0 12px 0;font-size:14px;'>
    Trước hết, chúng tôi xin gửi lời cảm ơn chân thành đến Quý khách vì đã quan tâm, tin tưởng
    và lựa chọn đồng hành cùng doanh nghiệp trong nhu cầu tư vấn và đặt hàng. Sự ủng hộ của Quý khách
    là động lực để đội ngũ MES không ngừng hoàn thiện chất lượng phục vụ, tối ưu quy trình sản xuất
    và nâng cao trải nghiệm trong từng đơn hàng.
  </p>
  <p style='margin:0 0 12px 0;font-size:14px;'>
    Chúng tôi luôn hướng tới việc cung cấp cho Quý khách giải pháp phù hợp, minh bạch về chi phí
    và đảm bảo tốt về tiến độ thực hiện. Trong trường hợp Quý khách cần điều chỉnh phương án,
    thay đổi quy cách, cập nhật số lượng hoặc cần được tư vấn thêm trước khi xác nhận,
    đội ngũ của chúng tôi luôn sẵn sàng hỗ trợ để cùng Quý khách lựa chọn phương án tối ưu nhất.
  </p>

  <p style='margin:0;font-size:14px;'>
    Một lần nữa, xin chân thành cảm ơn Quý khách đã dành thời gian làm việc cùng chúng tôi.
    Kính mời Quý khách tham khảo nội dung báo giá bên dưới để thuận tiện cho việc xác nhận,
    phản hồi hoặc tiếp tục trao đổi khi cần thiết.
  </p>
</div>";
        }

        private static string SecurePlainUrlBlock(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            var safeUrl = System.Net.WebUtility.HtmlEncode(url);

            var copyBox = "margin:10px auto 0 auto;max-width:640px;background:#f8fafc;border:1px dashed #cbd5e1;border-radius:10px;padding:10px 12px;text-align:left;";
            var copyTitle = "font-size:12px;color:#475569;font-weight:800;margin:0 0 6px 0;";
            var copyUrl = "font-size:12px;color:#0f172a;word-break:break-all;line-height:1.6;margin:0;font-family:Roboto, Arial, Helvetica, sans-serif;background:#ffffff;border:1px solid #e2e8f0;border-radius:8px;padding:10px 12px;user-select:all;-webkit-user-select:all;";

            return $@"
<div style='{copyBox}'>
  <p style='{copyTitle}'>Nếu đường link trên không hoạt động, hãy copy link bên dưới và dán vào trình duyệt:</p>
  <p style='{copyUrl}'>{safeUrl}</p>
</div>";
        }

        private static string QuoteEmailInner(order_request req, cost_estimate est, quote q, string? orderDetailUrl, bool showAction)
        {
            var delivery = req.delivery_date?.ToString("dd/MM/yyyy") ?? "N/A";
            var requestDateText = req.order_request_date?.ToString("dd/MM/yyyy HH:mm") ?? "N/A";
            var productName = req.product_name ?? "";
            var quantity = req.quantity ?? 0;

            var paperName = string.IsNullOrWhiteSpace(est.paper_name) ? "N/A" : est.paper_name;
            var coatingType = MapCoatingType(est.coating_type);
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

            var expiredAt = ResolveQuoteExpiredAt(req, q);
            bool isCustomerCopy = !string.IsNullOrEmpty(orderDetailUrl);

            string FormatVND(decimal? amount) => string.Format("{0:N0} đ", amount ?? 0).Replace(",", ".");

            var font = "font-family:Roboto, Arial, Helvetica, sans-serif; line-height:1.35;";
            var tableFixed = "width:100%;border-collapse:collapse;table-layout:fixed;";
            var tdLabel = "width:130px;white-space:nowrap;color:#64748b;font-size:13px;padding:7px 0;border-bottom:1px solid #eef2f7;vertical-align:top;";
            var tdValue = "color:#0f172a;font-size:13px;font-weight:700;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;vertical-align:top;word-break:break-word;";
            var tdValueNoWrap = tdValue + "white-space:nowrap;font-size:12px;";
            var tdValueLink = "color:#2563eb;font-size:13px;font-weight:800;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;vertical-align:top;word-break:break-word;";

            var card = "background:#ffffff;border:1px solid #cbd5e1;border-radius:12px;overflow:hidden;box-shadow:0 6px 20px rgba(15,23,42,0.08);";
            var header = "background:linear-gradient(90deg,#1e4f86 0%,#1d3f73 100%);padding:18px 20px;";
            var badge = "display:inline-block;background:rgba(255,255,255,0.18);color:#fff;padding:6px 10px;border-radius:6px;font-size:12px;font-weight:700;";
            var h1 = "color:#ffffff;font-size:18px;font-weight:800;margin:4px 0 0 0;letter-spacing:0.2px;";
            var smallTop = "color:#cfe6ff;font-size:11px;font-weight:800;letter-spacing:1.2px;text-transform:uppercase;";
            var bodyPad = "padding:18px 18px 16px 18px;";

            var sectionTitleBlue = "font-size:13px;font-weight:800;text-transform:uppercase;letter-spacing:0.6px;color:#2563eb;border-bottom:2px solid #bfdbfe;padding-bottom:6px;margin:0 0 10px 0;";
            var sectionTitleOrange = "font-size:13px;font-weight:800;text-transform:uppercase;letter-spacing:0.6px;color:#d97706;border-bottom:2px solid #fde68a;padding-bottom:6px;margin:0 0 10px 0;";
            var sectionTitleGreen = "font-size:13px;font-weight:800;text-transform:uppercase;letter-spacing:0.6px;color:#16a34a;border-bottom:2px solid #bbf7d0;padding-bottom:6px;margin:0 0 10px 0;";

            var boxCost = "background:#fff7ed;border:1px solid #fed7aa;border-radius:10px;padding:12px;";
            var line = "padding:6px 0;font-size:13px;color:#334155;";
            var money = "padding:6px 0;font-size:13px;color:#0f172a;font-weight:800;text-align:right;";
            var moneyRed = "padding:6px 0;font-size:13px;color:#dc2626;font-weight:900;text-align:right;";

            var totalLabel = "color:#64748b;font-size:13px;padding:6px 0;";
            var totalValue = "color:#0f172a;font-size:13px;font-weight:800;text-align:right;padding:6px 0;";
            var finalRowLeft = "font-weight:900;color:#0f172a;font-size:14px;padding-top:10px;";
            var finalRowRight = "font-weight:900;color:#1d4ed8;font-size:18px;text-align:right;padding-top:10px;";
            var vatNote = "text-align:right;font-size:11px;color:#ef4444;padding-top:4px;";

            var depositBoxCell = "background:#ecfdf5;border:1px solid #86efac;border-radius:10px;padding:12px;";
            var depositLeft = "color:#166534;font-weight:900;font-size:13px;";
            var depositRight = "color:#166534;font-weight:900;font-size:16px;text-align:right;";

            var warnBox = "background:#f1f5f9;border:1px dashed #94a3b8;border-radius:10px;padding:10px 12px;display:inline-block;";
            var warnText = "color:#475569;font-weight:800;font-size:12px;";

            var otherFeesRow = otherFees > 0
                ? $"<tr><td style='{line}'>Chi phí khác</td><td style='{money}'>{FormatVND(otherFees)}</td></tr>"
                : "";

            var rushAmountRow = rushAmount > 0
                ? $"<tr><td style='{line}'>Phụ thu giao gấp</td><td style='{money}'>{FormatVND(rushAmount)}</td></tr>"
                : "";

            var discountRow = (discountPercent > 0m || discountAmount > 0m)
                ? $@"
<tr>
  <td style='{totalLabel};border-bottom:1px dashed #cbd5e1;'>Giảm giá ({discountPercent:0.#}%)</td>
  <td style='{moneyRed};border-bottom:1px dashed #cbd5e1;'>- {FormatVND(discountAmount)}</td>
</tr>"
                : "";

            var expiryNoteHtml = QuoteExpiryNotice(expiredAt, includeAutoReject: true);

            string actionBlock = "";
            if (showAction)
            {
                if (isCustomerCopy)
                {
                    var copyUrlBlock = SecurePlainUrlBlock(orderDetailUrl);

                    actionBlock = $@"
{copyUrlBlock}

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
            }

            var rightColLayout = $@"
<div style='display:flex; flex-direction:column; justify-content:space-between; height:100%;'>
  <table width='100%' height='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; height:100%;'>
    <tr>
      <td valign='top'>
        <div style='{sectionTitleGreen}'>Tổng thanh toán</div>
        <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;'>
          <tr>
            <td style='{totalLabel}'>Tạm tính</td>
            <td style='{totalValue}'>{FormatVND(subtotal)}</td>
          </tr>
          {discountRow}
          <tr>
            <td style='{finalRowLeft}'>THÀNH TIỀN</td>
            <td style='{finalRowRight}'>{FormatVND(finalTotal)}</td>
          </tr>
          <tr>
            <td colspan='2' style='{vatNote}'>(Đã bao gồm VAT)</td>
          </tr>
        </table>
      </td>
    </tr>
    <tr>
      <td valign='bottom' style='height:100%; vertical-align:bottom;'>
        <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:separate;border-spacing:0; margin-top:16px;'>
          <tr>
            <td style='{depositBoxCell}'>
              <table width='100%' cellpadding='0' cellspacing='0' border='0'>
                <tr>
                  <td style='{depositLeft}'>Cần cọc:</td>
                  <td style='{depositRight}'>{FormatVND(deposit)}</td>
                </tr>
              </table>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</div>";

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
    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;'>
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
            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;'>
              <tr><td style='{line}'>Nguyên vật liệu</td><td style='{money}'>{FormatVND(materialCost)}</td></tr>
              <tr><td style='{line}'>Chi phí nhân công</td><td style='{money}'>{FormatVND(laborCost)}</td></tr>
              {otherFeesRow}
              {rushAmountRow}
            </table>
          </div>
        </td>
      </tr>
    </table>

    <div style='height:14px;'></div>

    <table width='100%' height='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; height:100%;'>
      <tr>
        <td width='50%' valign='top' style='padding-right:10px; height:100%;'>
          <div style='{sectionTitleBlue}'>Chi tiết sản phẩm</div>
          <table style='{tableFixed}' cellpadding='0' cellspacing='0' border='0'>
            <tr><td style='{tdLabel}'>Sản phẩm</td><td style='{tdValue}'>{productName}</td></tr>
            <tr><td style='{tdLabel}'>Số lượng</td><td style='{tdValue}'>{quantity:N0}</td></tr>
            <tr><td style='{tdLabel}'>Loại giấy</td><td style='{tdValue}'>{paperName}</td></tr>
            <tr><td style='{tdLabel}'>Phủ</td><td style='{tdValue}'>{coatingType}</td></tr>
            <tr><td style='{tdLabel}'>Sóng</td><td style='{tdValue}'>{waveType}</td></tr>
            <tr><td style='{tdLabel}'>Thiết kế</td><td style='{tdValueNoWrap}'>{designType}</td></tr>
            <tr><td style='{tdLabel}'>Giao dự kiến</td><td style='{tdValue}'>{delivery}</td></tr>
          </table>
        </td>

        <td width='50%' valign='top' style='padding-left:10px; height:100%;'>
          {rightColLayout}
        </td>
      </tr>
    </table>

    {actionBlock}
  </div>
</div>";
        }

        public static string QuoteEmail(order_request req, cost_estimate est, quote q, string orderDetailUrl)
        {
            var intro = QuoteIntro(req);
            var inner = QuoteEmailInner(req, est, q, orderDetailUrl, showAction: true);

            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <link href='https://fonts.googleapis.com/css2?family=Roboto:wght@400;500;700;800&display=swap' rel='stylesheet'>
  <style>
    @import url('https://fonts.googleapis.com/css2?family=Roboto:wght@400;500;700;800&display=swap');
  </style>
</head>
<body style='background-color:#f7fafc;padding:30px 0;font-family:Roboto, Arial, Helvetica, sans-serif;'>
  <div style='max-width:700px;margin:0 auto;'>
    {intro}
    {inner}
    <div style='background:linear-gradient(180deg,#edf2f7 0%,#e2e8f0 100%);padding:15px;text-align:center;font-size:12px;color:#64748b;border-radius:12px;margin-top:14px;'>
      Email này được gửi tự động từ hệ thống MES.
    </div>
  </div>
</body>
</html>";
        }

        public static string QuoteEmailCompare(order_request req, List<(cost_estimate est, quote q, string? checkoutUrl)> pairs)
        {
            if (pairs == null || pairs.Count == 0)
                return QuoteEmail(req, new cost_estimate(), new quote { created_at = AppTime.NowVnUnspecified() }, "");

            var left = pairs[0];
            var right = pairs.Count > 1 ? pairs[1] : ((cost_estimate est, quote q, string? checkoutUrl)?)null;

            var expiredAt = ResolveQuoteExpiredAt(req, left.q);
            var expiryBox = QuoteExpiryNotice(expiredAt, includeAutoReject: true);

            var leftHtml = QuoteEmailInner(req, left.est, left.q, left.checkoutUrl, showAction: false);
            var rightHtml = right.HasValue
                ? QuoteEmailInner(req, right.Value.est, right.Value.q, right.Value.checkoutUrl, showAction: false)
                : "";

            var isCustomerCopy = pairs.Any(x => !string.IsNullOrWhiteSpace(x.checkoutUrl));

            string sharedAction = "";
            if (!string.IsNullOrWhiteSpace(left.checkoutUrl))
            {
                sharedAction = $@"
<div style='margin-top:16px;'>
  {SecurePlainUrlBlock(left.checkoutUrl)}
</div>";
            }

            string compareLayoutHtml;
            if (pairs.Count == 1)
            {
                compareLayoutHtml = $@"
<table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;'>
  <tr>
    <td align='center' style='padding:0;'>
      <table width='750' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; max-width:720px; width:720px;'>
        <tr>
          <td style='padding:0;'>
            <div style='background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:10px;box-shadow:0 10px 22px rgba(15,23,42,0.06);'>
              {leftHtml}
            </div>
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>";
            }
            else
            {
                compareLayoutHtml = $@"
<table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;'>
  <tr>
    <td width='50%' valign='top' style='padding:0 10px 0 0;'>
      <div style='background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:10px;box-shadow:0 10px 22px rgba(15,23,42,0.06);'>
        {leftHtml}
      </div>
    </td>

    <td width='50%' valign='top' style='padding:0 0 0 10px;'>
      <div style='background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:10px;box-shadow:0 10px 22px rgba(15,23,42,0.06);'>
        {rightHtml}
      </div>
    </td>
  </tr>
</table>";
            }

            var introHtml = isCustomerCopy ? QuoteIntro(req) : "";

            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <link href='https://fonts.googleapis.com/css2?family=Roboto:wght@400;500;700;800&display=swap' rel='stylesheet'>
  <style>
    @import url('https://fonts.googleapis.com/css2?family=Roboto:wght@400;500;700;800&display=swap');
  </style>
</head>
<body style='margin:0;background-color:#f7fafc;padding:30px 0;font-family:Roboto, Arial, Helvetica, sans-serif;'>
  <div style='max-width:1100px;margin:0 auto;padding:0 12px;'>

    {introHtml}

    <div style='margin-bottom:18px;text-align:center;color:#0f172a;font-weight:800;font-size:18px;letter-spacing:0.2px;'>
      BÁO GIÁ ĐƠN HÀNG AM{req.order_request_id:D6}
    </div>

    {compareLayoutHtml}

    {sharedAction}
    {expiryBox}

    <div style='background:linear-gradient(180deg,#edf2f7 0%,#e2e8f0 100%);padding:15px;text-align:center;font-size:12px;color:#64748b;margin-top:16px;border-radius:12px;'>
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
    <tr><td style='border:1px solid transparent;padding:4px 8px;width:30%;'><b>Mã đơn hàng</b></td><td style='border:1px solid transparent;padding:4px 8px;'><span style='color:blue'>{order.code}</span></td></tr>
    <tr><td style='border:1px solid transparent;padding:4px 8px;'><b>Sản phẩm</b></td><td style='border:1px solid transparent;padding:4px 8px;'>{req.product_name}</td></tr>
    <tr><td style='border:1px solid transparent;padding:4px 8px;'><b>Số lượng</b></td><td style='border:1px solid transparent;padding:4px 8px;'>{req.quantity}</td></tr>
    <tr><td style='border:1px solid transparent;padding:4px 8px;'><b>Giá trị đơn hàng</b></td><td style='border:1px solid transparent;padding:4px 8px;'>{VND(est.final_total_cost)}</td></tr>
  </table>

  <hr style='border:none;border-top:1px solid #e5e7eb;margin:16px 0;' />
  <p>Bạn có thể theo dõi tiến độ sản xuất tại:<br/>{trackingUrl}</p>
  <p>Vui lòng lưu lại <b>mã đơn hàng</b> để tra cứu sau này.</p>
  <p>MES trân trọng!</p>
</div>";
        }

        public static string AcceptConsultantEmail(order_request req, order order)
        {
            return $@"
<div style='font-family:Arial,Helvetica,sans-serif;max-width:720px;margin:24px auto;line-height:1.6'>
  <h3 style='margin-top:0;'>KHÁCH HÀNG ĐÃ ĐỒNG Ý BÁO GIÁ</h3>

  <table style='border-collapse:collapse;width:100%;margin-top:8px;'>
    <tr><td style='border:1px solid transparent;padding:4px 8px;width:30%;'><b>Request ID</b></td><td style='border:1px solid transparent;padding:4px 8px;'>{req.order_request_id}</td></tr>
    <tr><td style='border:1px solid transparent;padding:4px 8px;'><b>Order Code</b></td><td style='border:1px solid transparent;padding:4px 8px;'><b>{order.code}</b></td></tr>
    <tr><td style='border:1px solid transparent;padding:4px 8px;'><b>Sản phẩm</b></td><td style='border:1px solid transparent;padding:4px 8px;'>{req.product_name}</td></tr>
    <tr><td style='border:1px solid transparent;padding:4px 8px;'><b>Số lượng</b></td><td style='border:1px solid transparent;padding:4px 8px;'>{req.quantity}</td></tr>
  </table>
</div>";
        }

        private static string QuoteExpiryNotice(DateTime expiredAt, bool includeAutoReject = true)
        {
            var wrap = "margin-top:14px;background:#fff7ed;border:1px solid #fed7aa;border-radius:12px;padding:12px 14px;";
            var title = "font-size:13px;color:#9a3412;font-weight:900;margin:0 0 6px 0;letter-spacing:0.2px;";
            var text = "margin:0;color:#7c2d12;font-size:12.5px;line-height:1.55;";
            var small = "margin:8px 0 0 0;color:#9a3412;font-size:12px;line-height:1.4;font-weight:700;";

            var autoRejectLine = includeAutoReject
                ? "Nếu sau thời hạn này bạn chưa phản hồi, hệ thống sẽ tự động ghi nhận là <b>từ chối báo giá</b>."
                : "Vui lòng phản hồi trong thời hạn hiệu lực để chúng tôi giữ đúng đơn giá và tiến độ.";

            return $@"
<div style='{wrap}'>
  <p style='{title}'>⏳ Lưu ý quan trọng về thời hạn báo giá</p>
  <p style='{text}'>
    Báo giá này có hiệu lực đến <b>{expiredAt:dd/MM/yyyy HH:mm}</b>.
    {autoRejectLine}
  </p>
  <p style='{small}'>
    Sau khi hết hạn, bạn vẫn có thể yêu cầu tạo báo giá mới — chi phí và thời gian giao hàng có thể thay đổi theo thời điểm.
  </p>
</div>";
        }

        private static DateTime ResolveQuoteExpiredAt(order_request req, quote q)
        {
            if (req.quote_expires_at.HasValue)
                return req.quote_expires_at.Value;

            if (req.verified_at.HasValue)
                return req.verified_at.Value.AddDays(7);

            return q.created_at.AddDays(7);
        }
    }
}