using AMMS.Infrastructure.Entities;
using System.Globalization;

namespace AMMS.Application.Helpers
{
    public static class DealEmailTemplates
    {
        private static string VND(decimal v)
            => string.Format(new CultureInfo("vi-VN"), "{0:N0} ₫", v);
        private const string EmailFontFamily = "Arial, Helvetica, sans-serif";
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
<div style='margin-top:18px;background:linear-gradient(135deg,#fff7ed 0%,#eff6ff 100%);border:1px solid #dbe7f3;border-radius:16px;padding:22px 24px;box-shadow:0 10px 28px rgba(15,23,42,0.06);font-family:'Arial, Helvetica, sans-serif;line-height:1.78;color:#334155;'>
  <div style='display:inline-block;background:linear-gradient(90deg,#f97316 0%,#2563eb 100%);color:#ffffff;font-size:11px;font-weight:800;letter-spacing:1px;text-transform:uppercase;padding:6px 12px;border-radius:999px;margin-bottom:12px;'>
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

            var safeUrl = System.Net.WebUtility.HtmlEncode(url);

            var copyBox = "margin:0 0 16px 0;max-width:100%;background:#f8fafc;border:1px dashed #cbd5e1;border-radius:12px;padding:12px 14px;text-align:left;";
            var copyTitle = "font-size:12px;color:#475569;font-weight:800;margin:0 0 8px 0;";
            var copyDesc = "font-size:12px;color:#334155;line-height:1.6;margin:0 0 8px 0;";
            var copyUrl = "font-size:12px;color:#0f172a;word-break:break-all;line-height:1.6;margin:0;font-family:'Arial, Helvetica, sans-serif;background:#ffffff;border:1px solid #e2e8f0;border-radius:8px;padding:10px 12px;user-select:all;-webkit-user-select:all;";

            return $@"
<div style='{copyBox}'>
  <p style='{copyTitle}'>🔗 Đường dẫn xác nhận báo giá</p>
  <p style='{copyDesc}'>
    Xin cảm ơn quý khách hàng đã tin tưởng và sử dụng dịch vụ của chúng tôi. Vui lòng copy đường dẫn bên dưới và dán vào tab mới của trình duyệt để tiếp tục xác nhận báo giá.
  </p>
  <p style='{copyUrl}'>{safeUrl}</p>
</div>";
        }

        private static string ContractLinksBlock(IEnumerable<cost_estimate> estimates)
        {
            var items = estimates
                .Where(x => !string.IsNullOrWhiteSpace(x.contract_file_path))
                .GroupBy(x => x.estimate_id)
                .Select(g => g.First())
                .OrderBy(x => x.estimate_id)
                .ToList();

            if (items.Count == 0)
                return "";

            var rows = string.Join("", items.Select(x =>
            {
                var rawUrl = x.contract_file_path!.Trim();
                var safeUrl = System.Net.WebUtility.HtmlEncode(rawUrl);

                return $@"
<tr>
  <td style='padding:10px 12px;border:1px solid #e2e8f0;font-size:12px;color:#0f172a;font-weight:800;white-space:nowrap;width:90px;vertical-align:top;'>
    E{x.estimate_id}
  </td>
  <td style='padding:10px 12px;border:1px solid #e2e8f0;font-size:12px;color:#2563eb;word-break:break-all;line-height:1.6;vertical-align:top;'>
    <a href='{rawUrl}' target='_blank' rel='noopener noreferrer' style='color:#2563eb;text-decoration:none;'>
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

        private static string QuoteEmailInner(order_request req, cost_estimate est, string? messageToCustomer)
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

            string FormatVND(decimal? amount) => string.Format("{0:N0} đ", amount ?? 0).Replace(",", ".");

            var font = $"font-family:{EmailFontFamily}; line-height:1.45;";
            var tableFixed = "width:100%;border-collapse:collapse;table-layout:fixed;";
            var tdLabel = "width:130px;white-space:nowrap;color:#64748b;font-size:13px;padding:7px 0;border-bottom:1px solid #eef2f7;vertical-align:top;";
            var tdValue = "color:#0f172a;font-size:13px;font-weight:700;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;vertical-align:top;word-break:break-word;overflow-wrap:anywhere;";
            var tdValueWrap = "color:#0f172a;font-size:12px;font-weight:600;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;vertical-align:top;white-space:normal;word-break:break-word;overflow-wrap:anywhere;line-height:1.45;";
            var tdValueLink = "color:#2563eb;font-size:13px;font-weight:700;padding:7px 0;border-bottom:1px solid #eef2f7;text-align:right;vertical-align:top;word-break:break-word;overflow-wrap:anywhere;";

            var card = "background:#ffffff; border:1px solid #e2e8f0; border-radius:16px; overflow:hidden; box-shadow:0 10px 25px rgba(0,0,0,0.05); margin-bottom:20px;";
            var header = "background:linear-gradient(90deg,#1e3a8a 0%,#2563eb 100%); padding:20px;";
            var badge = "display:inline-block;background:rgba(255,255,255,0.18);color:#fff;padding:6px 10px;border-radius:6px;font-size:12px;font-weight:700;";
            var h1 = "color:#ffffff;font-size:18px;font-weight:700;margin:4px 0 0 0;letter-spacing:0.2px;";
            var smallTop = "color:#cfe6ff;font-size:11px;font-weight:700;letter-spacing:1px;text-transform:uppercase;";
            var bodyPad = "padding:18px 18px 16px 18px;";

            var sectionTitleBlue = "font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:0.4px;color:#2563eb;border-bottom:2px solid #bfdbfe;padding-bottom:6px;margin:0 0 10px 0;";
            var sectionTitleOrange = "font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:0.4px;color:#d97706;border-bottom:2px solid #fde68a;padding-bottom:6px;margin:0 0 10px 0;";
            var sectionTitleGreen = "font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:0.4px;color:#16a34a;border-bottom:2px solid #bbf7d0;padding-bottom:6px;margin:0 0 10px 0;";

            var boxCost = "background:#fff7ed;border:1px solid #fed7aa;border-radius:10px;padding:12px;";
            var line = "padding:6px 0;font-size:13px;color:#334155;";
            var money = "padding:6px 0;font-size:13px;color:#0f172a;font-weight:700;text-align:right;";
            var moneyRed = "padding:6px 0;font-size:13px;color:#dc2626;font-weight:700;text-align:right;";

            var totalLabel = "color:#64748b;font-size:13px;padding:6px 0;";
            var totalValue = "color:#0f172a;font-size:13px;font-weight:700;text-align:right;padding:6px 0;";
            var finalRowLeft = "font-weight:700;color:#0f172a;font-size:14px;padding-top:10px;";
            var finalRowRight = "font-weight:700;color:#1d4ed8;font-size:18px;text-align:right;padding-top:10px;";
            var vatNote = "text-align:right;font-size:11px;color:#ef4444;padding-top:4px;";

            var depositBoxCell = "background:#ecfdf5;border:1px solid #86efac;border-radius:10px;padding:12px;";
            var depositLeft = "color:#166534;font-weight:700;font-size:13px;";
            var depositRight = "color:#166534;font-weight:700;font-size:16px;text-align:right;";

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

            string messageBoxHtml = "";
            if (!string.IsNullOrWhiteSpace(messageToCustomer))
            {
                var msgBoxStyle = "margin:0 0 20px 0;background-color:#f8fafc;border:1px solid #e2e8f0;border-left:4px solid #3b82f6;border-radius:12px;padding:16px;";
                var msgTitleStyle = "color:#0369a1;font-size:13px;font-weight:800;text-transform:uppercase;margin-bottom:8px;display:block;letter-spacing:0.5px;";
                var msgContentStyle = "color:#1e293b;font-size:14px;line-height:1.6;margin:0;font-style:italic;";

                messageBoxHtml = $@"
<div style='{msgBoxStyle}'>
    <span style='{msgTitleStyle}'>💬 Lời nhắn từ chuyên viên tư vấn:</span>
    <p style='{msgContentStyle}'>""{System.Net.WebUtility.HtmlEncode(messageToCustomer.Trim())}""</p>
</div>";
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

    <div style='{bodyPad}background-color:#ffffff;'>
        {messageBoxHtml}

        <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;'>
            <tr>
                <td width='50%' style='vertical-align:top;padding-right:12px;'>
                    <div style='{sectionTitleBlue}'>Thông tin đơn hàng</div>
                    <table style='{tableFixed}' cellpadding='0' cellspacing='0' border='0'>
                        <tr><td style='{tdLabel}'>Ngày yêu cầu</td><td style='{tdValue}'>{requestDateText}</td></tr>
                        <tr><td style='{tdLabel}'>Người yêu cầu</td><td style='{tdValue};text-transform:uppercase'>{req.customer_name}</td></tr>
                        <tr><td style='{tdLabel}'>Số điện thoại</td><td style='{tdValue}'>{req.customer_phone}</td></tr>
                        <tr><td style='{tdLabel}'>Email</td><td style='{tdValueLink}'>{req.customer_email}</td></tr>
                    </table>
                </td>

                <td width='50%' style='vertical-align:top;padding-left:12px;'>
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

        <div style='height:20px;line-height:20px;font-size:0;'>&nbsp;</div>

        <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;table-layout:fixed;'>
    <tr>
        <td width='50%' valign='top' style='padding-right:12px;vertical-align:top;'>
            <div style='{sectionTitleBlue}'>Chi tiết sản phẩm</div>
            <table style='{tableFixed}' cellpadding='0' cellspacing='0' border='0'>
                <tr><td style='{tdLabel}'>Sản phẩm</td><td style='{tdValue}'>{productName}</td></tr>
                <tr><td style='{tdLabel}'>Số lượng</td><td style='{tdValue}'>{quantity:N0}</td></tr>
                <tr><td style='{tdLabel}'>Loại giấy</td><td style='{tdValue}'>{paperName}</td></tr>
                <tr><td style='{tdLabel}'>Phủ</td><td style='{tdValue}'>{coatingType}</td></tr>
                <tr><td style='{tdLabel}'>Sóng</td><td style='{tdValue}'>{waveType}</td></tr>
                <tr><td style='{tdLabel}'>Thiết kế</td><td style='{tdValueWrap}'>{designType}</td></tr>
                <tr><td style='{tdLabel}'>Giao dự kiến</td><td style='{tdValue}'>{delivery}</td></tr>
            </table>
        </td>

        <td width='50%' valign='top' style='padding-left:12px;vertical-align:top;'>
            <div style='{sectionTitleGreen}'>Tổng thanh toán</div>

            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;table-layout:fixed;'>
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

                <tr>
                    <td colspan='2' style='padding-top:18px;'>
                        <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:separate;border-spacing:0;'>
                            <tr>
                                <td style='{depositBoxCell}'>
                                    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse;'>
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
        </td>
    </tr>
</table>
    </div>
</div>";
        }

        public static string QuoteEmail(order_request req, cost_estimate est, quote q, string orderDetailUrl)
        {
            var topCopyBlock = SecurePlainUrlBlock(orderDetailUrl);
            var inner = QuoteEmailInner(req, est, req.message_to_customer);
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
<body style='background-color:#f7fafc;padding:30px 0;font-family:'Arial, Helvetica, sans-serif;'>
  <div style='max-width:700px;margin:0 auto;'>
    {topCopyBlock}
    {inner}
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

            var left = pairs[0];
            var right = pairs.Count > 1 ? pairs[1] : ((cost_estimate est, quote q, string? checkoutUrl)?)null;

            var expiryBox = QuoteExpiryNotice(ResolveQuoteExpiredAt(req, left.q), includeAutoReject: true);
            var contractBlock = ContractLinksBlock(pairs.Select(x => x.est).ToList());

            var leftHtml = QuoteEmailInner(req, left.est, req.message_to_customer);

            var rightHtml = right.HasValue
                ? QuoteEmailInner(req, right.Value.est, req.message_to_customer)
                : "";

            var isCustomerCopy = pairs.Any(x => !string.IsNullOrWhiteSpace(x.checkoutUrl));

            string sharedAction = "";
            if (!string.IsNullOrWhiteSpace(left.checkoutUrl))
            {
                sharedAction = SecurePlainUrlBlock(left.checkoutUrl);
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

            var closingNoteHtml = isCustomerCopy ? QuoteIntro(req) : "";

            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
</head>
<body style='margin:0;background-color:#f7fafc;padding:30px 0;font-family:'Arial, Helvetica, sans-serif;'>
  <div style='max-width:1100px;margin:0 auto;padding:0 12px;'>

    <div style='margin-bottom:18px;text-align:center;color:#0f172a;font-weight:800;font-size:18px;letter-spacing:0.2px;'>
      BÁO GIÁ ĐƠN HÀNG AM{req.order_request_id:D6}
    </div>

    {sharedAction}
    {compareLayoutHtml}
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