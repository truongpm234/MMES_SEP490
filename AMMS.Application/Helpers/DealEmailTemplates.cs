using AMMS.Infrastructure.Entities;
using AMMS.Shared.Helpers;
using System.Globalization;

namespace AMMS.Application.Helpers
{
    public static class DealEmailTemplates
    {
        private static string VND(decimal v)
            => string.Format(new CultureInfo("vi-VN"), "{0:N0} ₫", v);

        private const string EmailFontFamily = "\"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif";
        
        private static string QuoteIntro(order_request req)
        {
            var customerName = string.IsNullOrWhiteSpace(req.customer_name)
                ? "Quý khách"
                : req.customer_name!.Trim();

            return $@"
<div style='margin-top:28px;background:linear-gradient(135deg,#fff7ed 0%,#eff6ff 100%);border:1px solid #dbe7f3;border-radius:16px;padding:22px 24px;box-shadow:0 10px 28px rgba(15,23,42,0.06);font-family:{EmailFontFamily};line-height:1.78;color:#334155;'>
  <div style='display:inline-block;background:linear-gradient(90deg,#f97316 0%,#2563eb 100%);color:#ffffff;font-size:11px;font-weight:800;letter-spacing:1px;text-transform:uppercase;padding:12px 12px;border-radius:999px;margin-bottom:12px;'>
    MES CARE
  </div>

  <div style='font-size:20px;font-weight:800;color:#1e3a8a;margin-bottom:10px;letter-spacing:0.2px;'>
    Cảm ơn Quý khách đã tin tưởng và sử dụng dịch vụ của chúng tôi
  </div>

  <p style='margin:0 0 12px 0;font-size:14px;'>
    Kính gửi <b>quý khách hàng</b>,
  </p>

  <p style='margin:0 0 12px 0;font-size:14px;'>
    Chúng tôi chân thành cảm ơn Quý khách đã dành thời gian tham khảo nội dung báo giá ở trên.
    Toàn bộ thông tin về sản phẩm, vật liệu, quy cách, số lượng và chi phí đã được trình bày
    để Quý khách thuận tiện kiểm tra trước khi xác nhận.
  </p>

  <p style='margin:0 0 12px 0;font-size:14px;'>
    Nếu Quý khách cần điều chỉnh phương án, thay đổi loại giấy, cập nhật quy cách, số lượng
    hoặc cần tư vấn thêm để lựa chọn phương án phù hợp hơn, đội ngũ của chúng tôi luôn sẵn sàng hỗ trợ.
  </p>

  <p style='margin:0;font-size:14px;color:#0f172a;font-weight:600;'>
    Một lần nữa, xin chân thành cảm ơn Quý khách đã tin tưởng và đồng hành cùng MES.
  </p>
</div>";
        }

        private static string SecurePlainUrlBlock(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            var encodedUrl = System.Net.WebUtility.HtmlEncode(url);

            var safeUrl = encodedUrl
                .Replace("://", "<span>://</span>")
                .Replace(".", "<span>.</span>");

            var copyBox = "margin:0 0 24px 0;max-width:100%;background:#f0f9ff;border:2px solid #bae6fd;border-radius:12px;padding:24px 20px;text-align:left;box-shadow:0 4px 15px rgba(0,0,0,0.04);";

            var copyTitle = "font-size:22px;color:#0369a1;font-weight:900;margin:0 0 12px 0;line-height:1.4;";

            var copyDesc = "font-size:15px;color:#334155;line-height:1.6;margin:0 0 16px 0;";

            var copyUrl = $"font-size:20px;color:#0284c7;word-break:break-all;line-height:1.6;margin:0;font-family:{EmailFontFamily};font-weight:900;background:#ffffff;border:2px dashed #7dd3fc;border-radius:8px;padding:16px 20px;user-select:all;-webkit-user-select:all;text-decoration:none;cursor:text;text-align:center;";

            return $@"
<div style='{copyBox}'>
  <p style='{copyTitle}'>📌 Đường dẫn xác nhận báo giá</p>
  <p style='{copyDesc}'>
    Xin cảm ơn quý khách hàng đã tin tưởng và sử dụng dịch vụ của chúng tôi. Vui lòng <b>copy đường dẫn bên dưới</b> và dán vào tab mới của trình duyệt để tiếp tục xác nhận.<br> <b>Lưu ý bảo mật</b>: Quý khách vui lòng chỉ thực hiện thanh toán thông qua website theo đường link chính thức bên dưới. Chúng tôi không yêu cầu thanh toán hay giao dịch qua bất kỳ kênh trung gian hoặc tài khoản cá nhân nào khác. Công ty sẽ không chịu trách nhiệm giải quyết đối với bất kỳ rủi ro hay tổn thất nào phát sinh nếu quý khách thực hiện giao dịch ngoài hệ thống này.
  </p>
  <div style='{copyUrl}'><a></a>{safeUrl}</div>
</div>";
        }

        private static string Safe(string? s)
    => System.Net.WebUtility.HtmlEncode((s ?? "").Trim());

        private static string BuildRequestSummaryBlock(order_request req)
        {
            return $@"
<div style='background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:16px 18px;margin:16px 0;box-shadow:0 8px 18px rgba(15,23,42,0.05);'>
  <div style='font-size:13px;font-weight:900;color:#1e3a8a;text-transform:uppercase;margin-bottom:10px;'>Thông tin request</div>
  <table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;'>
    <tr><td style='padding:6px 0;width:35%;font-size:12px;color:#64748b;'>Request ID</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>AM{req.order_request_id:D6}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Khách hàng</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(req.customer_name)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>SĐT</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(req.customer_phone)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Email</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(req.customer_email)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Địa chỉ</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(req.detail_address)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Sản phẩm</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(req.product_name)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Số lượng</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{req.quantity:N0}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Loại sản phẩm</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(req.product_type)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Số bản kẽm</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{req.number_of_plates}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Ngày yêu cầu</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{req.order_request_date:dd/MM/yyyy HH:mm}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Ngày giao</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{req.delivery_date:dd/MM/yyyy}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Ghi chú KH</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(req.description)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Message cho KH</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(req.message_to_customer)}</td></tr>
  </table>
</div>";
        }

        private static string BuildEstimateBlock(cost_estimate est, quote q)
        {
            string contractLink = string.IsNullOrWhiteSpace(est.consultant_contract_path)
                ? ""
                : $@"
<tr>
  <td style='padding:6px 0;font-size:12px;color:#64748b;'>Hợp đồng consultant</td>
  <td style='padding:6px 0;font-size:12px;color:#2563eb;font-weight:700;word-break:break-all;'>{Safe(est.consultant_contract_path)}</td>
</tr>";

            return $@"
<div style='background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:16px 18px;margin:16px 0;box-shadow:0 8px 18px rgba(15,23,42,0.05);'>
  <div style='font-size:13px;font-weight:900;color:#b45309;text-transform:uppercase;margin-bottom:10px;'>Chi tiết báo giá E{est.estimate_id}</div>
  <table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;'>
    <tr><td style='padding:6px 0;width:35%;font-size:12px;color:#64748b;'>Quote ID</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{q.quote_id}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Tổng tiền</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{VND(est.final_total_cost)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Tiền cọc</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{VND(est.deposit_amount)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Subtotal</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{VND(est.subtotal)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Discount %</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{est.discount_percent}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Discount amount</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{VND(est.discount_amount)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Paper code</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(est.paper_code)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Paper name</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(est.paper_name)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Coating</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(est.coating_type)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Wave</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(est.wave_type)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Production processes</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(est.production_processes)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Material cost</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{VND(est.material_cost)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Base cost</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{VND(est.base_cost)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Design cost</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{VND(est.design_cost)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Rush amount</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{VND(est.rush_amount)}</td></tr>
    <tr><td style='padding:6px 0;font-size:12px;color:#64748b;'>Cost note</td><td style='padding:6px 0;font-size:12px;color:#0f172a;font-weight:700;'>{Safe(est.cost_note)}</td></tr>
    {contractLink}
  </table>
</div>";
        }

        private static string ContractLinksBlock(IEnumerable<cost_estimate> estimates)
        {
            var items = estimates
                .Where(x => !string.IsNullOrWhiteSpace(x.consultant_contract_path))
                .GroupBy(x => x.estimate_id)
                .Select(g => g.First())
                .OrderBy(x => x.estimate_id)
                .ToList();

            if (items.Count == 0)
                return "";

            var rows = string.Join("", items.Select(x =>
            {
                var rawUrl = x.consultant_contract_path!.Trim();
                var safeUrl = System.Net.WebUtility.HtmlEncode(rawUrl);

                return $@"
<tr>
  <td style='padding:10px 12px;border:1px solid #e2e8f0;font-size:12px;color:#0f172a;font-weight:800;white-space:nowrap;width:90px;vertical-align:top;'>
    E{x.estimate_id}
  </td>
  <td style='padding:10px 12px;border:1px solid #e2e8f0;font-size:12px;color:#2563eb;word-break:break-all;line-height:1.6;vertical-align:top;'>
      {safeUrl}
    </a>
  </td>
</tr>";
            }));

            return $@"
<div style='margin-top:16px;background:linear-gradient(135deg,#ecfeff 0%,#f8fafc 100%);border:1px solid #cbd5e1;border-radius:14px;padding:16px;box-shadow:0 8px 18px rgba(15,23,42,0.05);'>
  <p style='margin:0 0 8px 0;font-size:13px;color:#0f766e;font-weight:900;letter-spacing:0.2px;'>
    📄 Đường dẫn hợp đồng
  </p>

  <p style='margin:0 0 12px 0;font-size:12.5px;color:#334155;line-height:1.65;'>
    Hợp đồng được đặt bên dưới phần báo giá để Quý khách tiện đối chiếu.
    Vui lòng mở đường dẫn tương ứng của từng phương án báo giá.
  </p>

  <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;background:#ffffff;border-radius:10px;overflow:hidden;table-layout:fixed;'>
    <thead>
      <tr style='background:#f8fafc;'>
        <th style='padding:10px 12px;border:1px solid #e2e8f0;text-align:left;font-size:12px;color:#475569;'>Báo giá</th>
        <th style='padding:10px 12px;border:1px solid #e2e8f0;text-align:left;font-size:12px;color:#475569;'>Đường dẫn hợp đồng</th>
      </tr>
    </thead>
    <tbody>
      {rows}
    </tbody>
  </table>
</div>";
        }
        
        public static string QuoteEmail(order_request req, cost_estimate est, quote q, string orderDetailUrl)
        {
            var topCopyBlock = SecurePlainUrlBlock(orderDetailUrl);
            var contractBlock = ContractLinksBlock(new[] { est });
            var expiryBox = QuoteExpiryNotice(ResolveQuoteExpiredAt(req, q), includeAutoReject: true);
            var closingNote = QuoteIntro(req);

            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
</head>
<body style='margin:0;background-color:#f7fafc;padding:30px 0;'>
  <div style='max-width:700px;margin:0 auto;padding:0 12px;font-family:{EmailFontFamily};'>
    {topCopyBlock}
    {contractBlock}
    {expiryBox}
    {closingNote}
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

            var first = pairs[0];
            var isCustomerCopy = pairs.Any(x => !string.IsNullOrWhiteSpace(x.checkoutUrl));

            var sharedAction = "";
            if (isCustomerCopy)
            {
                var firstUrl = pairs
                    .Select(x => x.checkoutUrl)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (!string.IsNullOrWhiteSpace(firstUrl))
                    sharedAction = SecurePlainUrlBlock(firstUrl);
            }

            var contractBlock = ContractLinksBlock(pairs.Select(x => x.est).ToList());
            var expiryBox = QuoteExpiryNotice(ResolveQuoteExpiredAt(req, first.q), includeAutoReject: true);

            // Chỉ consultant mới thấy 2 block này
            var requestBlock = isCustomerCopy ? "" : BuildRequestSummaryBlock(req);
            var estimateBlocks = isCustomerCopy
                ? ""
                : string.Join("", pairs
                    .OrderBy(x => x.est.estimate_id)
                    .Select(x => BuildEstimateBlock(x.est, x.q)));

            var closingNoteHtml = isCustomerCopy ? QuoteIntro(req) : "";
            var contentWidth = isCustomerCopy ? "700px" : "1100px";

            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
</head>
<body style='margin:0;background-color:#f7fafc;padding:30px 0;'>
  <div style='max-width:{contentWidth};margin:0 auto;padding:0 12px;font-family:{EmailFontFamily};'>
    <div style='margin-bottom:18px;text-align:center;color:#0f172a;font-weight:900;font-size:20px;letter-spacing:0.2px;'>
      BÁO GIÁ ĐƠN HÀNG AM{req.order_request_id:D6}
    </div>

    {sharedAction}
    {requestBlock}
    {estimateBlocks}
    {contractBlock}
    {expiryBox}
    {closingNoteHtml}

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
<div style='font-family:{EmailFontFamily};max-width:720px;margin:24px auto;line-height:1.6'>
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
<div style='font-family:{EmailFontFamily};max-width:720px;margin:24px auto;line-height:1.6'>
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

        public static string RequestResignContractUploadEmail(
    order_request req,
    cost_estimate? est,
    string uploadUrl,
    string customMessage)
        {
            var customerName = string.IsNullOrWhiteSpace(req.customer_name)
                ? "Quý khách"
                : Safe(req.customer_name);

            var productName = Safe(req.product_name);
            var phone = Safe(req.customer_phone);
            var email = Safe(req.customer_email);
            var address = Safe(req.detail_address);
            var deliveryDate = req.delivery_date.HasValue
                ? req.delivery_date.Value.ToString("dd/MM/yyyy")
                : "N/A";

            var contractUrl = est != null && !string.IsNullOrWhiteSpace(est.consultant_contract_path)
                ? $@"
<tr>
  <td style='padding:8px 0;width:36%;font-size:12px;color:#64748b;vertical-align:top;'>Hợp đồng tham chiếu</td>
  <td style='padding:8px 0;font-size:12px;color:#2563eb;font-weight:700;word-break:break-all;line-height:1.6;'>
    {Safe(est.consultant_contract_path)}
  </td>
</tr>"
                : "";

            var safeUploadUrl = Safe(uploadUrl);
            var safeMessage = Safe(customMessage);

            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
</head>
<body style='margin:0;background:#f8fafc;padding:30px 0;font-family:{EmailFontFamily};'>
  <div style='max-width:720px;margin:0 auto;padding:0 12px;'>
    
    <div style='background:linear-gradient(135deg,#dc2626 0%,#ea580c 100%);padding:28px 24px;border-radius:18px 18px 0 0;text-align:center;color:#fff;box-shadow:0 10px 30px rgba(15,23,42,0.12);'>
      <div style='font-size:12px;font-weight:900;letter-spacing:1.2px;text-transform:uppercase;opacity:0.95;'>MES CONTRACT REVIEW</div>
      <div style='margin-top:10px;font-size:24px;font-weight:900;line-height:1.35;'>Yêu cầu tải lên lại hợp đồng</div>
      <div style='margin-top:8px;font-size:13px;opacity:0.92;'>Request AM{req.order_request_id:D6}</div>
    </div>

    <div style='background:#ffffff;padding:26px 24px 22px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 18px 18px;box-shadow:0 12px 30px rgba(15,23,42,0.06);'>
      <p style='margin:0 0 14px 0;font-size:14px;color:#334155;line-height:1.7;'>
        Chào <b>{customerName}</b>,
      </p>

      <p style='margin:0 0 14px 0;font-size:14px;color:#334155;line-height:1.7;'>
        Sau khi kiểm tra hợp đồng của đơn hàng <b>AM{req.order_request_id:D6}</b>, chúng tôi nhận thấy tài liệu hiện tại cần được điều chỉnh / ký lại để đúng quy trình xử lý.
      </p>

      <div style='background:#fff7ed;border:1px solid #fdba74;border-radius:14px;padding:16px 18px;margin:18px 0;'>
        <div style='font-size:13px;font-weight:900;color:#c2410c;text-transform:uppercase;margin-bottom:8px;'>Nội dung cần khách hàng thực hiện</div>
        <div style='font-size:13px;color:#7c2d12;line-height:1.7;'>
          {safeMessage}
        </div>
      </div>

      <div style='background:#eff6ff;border:1px solid #bfdbfe;border-radius:14px;padding:18px;margin:18px 0;'>
        <div style='font-size:13px;font-weight:900;color:#1d4ed8;text-transform:uppercase;margin-bottom:10px;'>Link tải lên lại hợp đồng</div>
        <p style='margin:0 0 12px 0;font-size:13px;color:#334155;line-height:1.7;'>
          Vui lòng copy đường dẫn bên dưới và mở bằng trình duyệt để tải lên bản hợp đồng mới:
        </p>
        <div style='background:#ffffff;border:2px dashed #93c5fd;border-radius:10px;padding:14px 16px;font-size:14px;font-weight:800;color:#2563eb;word-break:break-all;line-height:1.7;user-select:all;-webkit-user-select:all;'>
          {safeUploadUrl}
        </div>
      </div>

      <div style='background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;padding:16px 18px;margin:18px 0;box-shadow:0 6px 18px rgba(15,23,42,0.04);'>
        <div style='font-size:13px;font-weight:900;color:#0f172a;text-transform:uppercase;margin-bottom:10px;'>Thông tin đơn hàng</div>
        <table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;'>
          <tr><td style='padding:8px 0;width:36%;font-size:12px;color:#64748b;'>Mã request</td><td style='padding:8px 0;font-size:12px;color:#0f172a;font-weight:700;'>AM{req.order_request_id:D6}</td></tr>
          <tr><td style='padding:8px 0;font-size:12px;color:#64748b;'>Khách hàng</td><td style='padding:8px 0;font-size:12px;color:#0f172a;font-weight:700;'>{customerName}</td></tr>
          <tr><td style='padding:8px 0;font-size:12px;color:#64748b;'>Số điện thoại</td><td style='padding:8px 0;font-size:12px;color:#0f172a;font-weight:700;'>{phone}</td></tr>
          <tr><td style='padding:8px 0;font-size:12px;color:#64748b;'>Email</td><td style='padding:8px 0;font-size:12px;color:#0f172a;font-weight:700;'>{email}</td></tr>
          <tr><td style='padding:8px 0;font-size:12px;color:#64748b;'>Sản phẩm</td><td style='padding:8px 0;font-size:12px;color:#0f172a;font-weight:700;'>{productName}</td></tr>
          <tr><td style='padding:8px 0;font-size:12px;color:#64748b;'>Ngày giao</td><td style='padding:8px 0;font-size:12px;color:#0f172a;font-weight:700;'>{deliveryDate}</td></tr>
          <tr><td style='padding:8px 0;font-size:12px;color:#64748b;'>Địa chỉ</td><td style='padding:8px 0;font-size:12px;color:#0f172a;font-weight:700;'>{address}</td></tr>
          {contractUrl}
        </table>
      </div>

      <div style='margin-top:18px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:14px;padding:14px 16px;'>
        <div style='font-size:12px;color:#475569;line-height:1.7;'>
          Sau khi tải lên lại hợp đồng, hệ thống sẽ chuyển sang bước kiểm tra lại để tiếp tục xử lý bố cục và sản xuất.
        </div>
      </div>
    </div>

    <div style='margin-top:14px;background:linear-gradient(180deg,#edf2f7 0%,#e2e8f0 100%);padding:15px;text-align:center;font-size:12px;color:#64748b;border-radius:12px;'>
      Email này được gửi tự động từ hệ thống MES.
    </div>
  </div>
</body>
</html>";
        }
    }
}