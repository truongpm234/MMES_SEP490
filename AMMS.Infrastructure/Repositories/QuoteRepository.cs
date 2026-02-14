using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Email;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Repositories
{
    public class QuoteRepository : IQuoteRepository
    {
        private readonly AppDbContext _context;

        public QuoteRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(quote entity)
        {
            await _context.quotes.AddAsync(entity);
        }
        public async Task<quote?> GetByIdAsync(int id)
        {
            return await _context.quotes.FirstOrDefaultAsync(x => x.quote_id == id);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

        public async Task<QuoteEmailPreviewResponse> BuildPreviewAsync(int quoteId, CancellationToken ct = default)
        {
            var q = await _context.quotes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.quote_id == quoteId, ct)
                ?? throw new Exception($"Quote not found id={quoteId}");

            var est = await _context.cost_estimates.AsNoTracking()
                .Include(x => x.process_costs)
                .FirstOrDefaultAsync(x => x.estimate_id == q.estimate_id, ct) ?? throw new Exception($"Estimate not found id={q.estimate_id}");

            var req = await _context.order_requests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.order_request_id == (q.order_request_id ?? est.order_request_id), ct) ?? throw new Exception($"OrderRequest not found");

            var paperName = string.IsNullOrWhiteSpace(est.paper_name) ? "N/A" : est.paper_name;
            var coatingType = string.IsNullOrWhiteSpace(est.coating_type) ? "N/A" : est.coating_type;
            var waveType = string.IsNullOrWhiteSpace(est.wave_type) ? "N/A" : est.wave_type;

            var designType = req.is_send_design == true ? "Tự gửi file thiết kế" : "Sử dụng bản thiết kế của doanh nghiệp";

            var materialCost = est.paper_cost + est.ink_cost + est.coating_glue_cost + est.mounting_glue_cost + est.lamination_cost;
            var laborCost = est.process_costs != null ? est.process_costs
                    .Where(p => p.estimate_id == est.estimate_id)
                    .Sum(p => p.total_cost) : 0m;

            var otherFees = est.design_cost;
            var rushAmount = est.rush_amount;
            var subtotal = est.subtotal;
            var finalTotal = est.final_total_cost;
            var discountPercent = est.discount_percent;
            var discountAmount = est.discount_amount;
            var deposit = est.deposit_amount;

            var deliveryText = req.delivery_date?.ToString("dd/MM/yyyy") ?? "N/A";
            var requestDateText = req.order_request_date?.ToString("dd/MM/yyyy HH:mm") ?? "N/A";

            var expiredAt = q.created_at.AddHours(24);
            var expiredAtText = expiredAt.ToString("dd/MM/yyyy HH:mm");
            var productionProcessText = BuildProductionProcessText_SameAsTemplate(req, est);

            var res = new QuoteEmailPreviewResponse
            {
                order_request_id = req.order_request_id,
                estimate_id = est.estimate_id,
                quote_id = q.quote_id,
                customer_name = req.customer_name,
                customer_phone = req.customer_phone,
                customer_email = req.customer_email,
                detail_address = req.detail_address,
                delivery_date = req.delivery_date,
                order_request_date = req.order_request_date,
                product_name = req.product_name,
                quantity = req.quantity ?? 0,
                paper_name = paperName,
                coating_type = coatingType,
                wave_type = waveType,
                is_send_design = req.is_send_design ?? false,
                quote_created_at = q.created_at,
                quote_expired_at = expiredAt,
                material_cost = materialCost,
                labor_cost = laborCost,
                other_fees = otherFees,
                rush_amount = rushAmount,
                subtotal = subtotal,
                final_total = finalTotal,
                discount_percent = discountPercent,
                discount_amount = discountAmount,
                deposit = deposit,
                design_type_text = designType,
                production_process_text = productionProcessText,
                delivery_text = deliveryText,
                request_date_text = requestDateText,
                quote_expired_at_text = expiredAtText
            };
            return res;
        }

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

        private static string BuildProductionProcessText_SameAsTemplate(order_request req, cost_estimate est)
        {
            var codes = new List<string>();

            if (!string.IsNullOrWhiteSpace(req.production_processes))
            {
                codes = req.production_processes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }
            else if (est.process_costs is { Count: > 0 })
            {
                codes = est.process_costs
                    .Select(p => p.process_code)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();
            }

            if (codes.Count == 0)
                return "Không có / Không áp dụng";

            return string.Join(", ", codes.Select(MapProcessCode));
        }
    }
}
