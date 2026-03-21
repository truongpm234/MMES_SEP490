using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Estimates;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AMMS.Infrastructure.Repositories
{
    public class BaseConfigRepository : IBaseConfigRepository
    {
        private readonly AppDbContext _db;

        public BaseConfigRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<EstimateBaseConfigDto> GetAsync(CancellationToken ct)
        {
            var rows = await _db.Set<estimate_config>()
                .AsNoTracking()
                .ToListAsync(ct);

            var dto = new EstimateBaseConfigDto
            {
                MaterialPrices = MapMaterialPrices(rows),
                MaterialRates = MapMaterialRates(rows),
                WasteRules = MapWasteRules(rows),
                SystemParameters = MapSystemParameters(rows),
                ProcessCosts = MapProcessCosts(rows),
                Design = MapDesign(rows),
                PlatePrices = MapPlatePrices(rows)
            };

            return dto;
        }

        private static PlatePriceConfig MapPlatePrices(List<estimate_config> rows)
        {
            var items = rows
                .Where(x => x.config_group == "platePrices")
                .OrderBy(x => GetPlateSortOrder(x.config_key))
                .ThenBy(x => x.config_key)
                .Select(x => new PlatePriceItemDto
                {
                    key = x.config_key,
                    category = GetPlateCategory(x.config_key),
                    category_text = GetPlateCategoryText(x.config_key),
                    size_text = x.value_text ?? "",
                    width_cm = GetPlateWidth(x.config_key),
                    height_cm = GetPlateHeight(x.config_key),
                    price_per_plate = x.value_num ?? 0
                })
                .ToList();

            return new PlatePriceConfig
            {
                items = items,
                small = items.Where(x => x.category == "small").ToList(),
                medium = items.Where(x => x.category == "medium").ToList(),
                large = items.Where(x => x.category == "large").ToList(),
                xlarge = items.Where(x => x.category == "xlarge").ToList()
            };
        }

        private static MaterialPriceConfig MapMaterialPrices(List<estimate_config> rows)
        {
            decimal GetNum(string group, string key) =>
                rows.FirstOrDefault(x => x.config_group == group && x.config_key == key)?.value_num ?? 0;

            return new MaterialPriceConfig
            {
                ink_price_per_kg = GetNum("materialPrices", "ink_price_per_kg"),
                coating_glue_keo_nuoc_per_kg = GetNum("materialPrices", "coating_glue_keo_nuoc_per_kg"),
                coating_glue_keo_dau_per_kg = GetNum("materialPrices", "coating_glue_keo_dau_per_kg"),
                mounting_glue_per_kg = GetNum("materialPrices", "mounting_glue_per_kg"),
                lamination_per_kg = GetNum("materialPrices", "lamination_per_kg")
            };
        }

        private static MaterialRateConfig MapMaterialRates(List<estimate_config> rows)
        {
            decimal GetNum(string group, string key) =>
                rows.FirstOrDefault(x => x.config_group == group && x.config_key == key)?.value_num ?? 0;

            return new MaterialRateConfig
            {
                ink_rate_gach_noi_dia = GetNum("materialRates", "ink_rate_gach_noi_dia"),
                ink_rate_gach_xk_don_gian = GetNum("materialRates", "ink_rate_gach_xk_don_gian"),
                ink_rate_hop_mau = GetNum("materialRates", "ink_rate_hop_mau"),
                ink_rate_gach_nhieu_mau = GetNum("materialRates", "ink_rate_gach_nhieu_mau"),
                coating_glue_rate_keo_nuoc = GetNum("materialRates", "coating_glue_rate_keo_nuoc"),
                coating_glue_rate_keo_dau = GetNum("materialRates", "coating_glue_rate_keo_dau"),
                mounting_glue_rate = GetNum("materialRates", "mounting_glue_rate"),
                lamination_rate_12mic = GetNum("materialRates", "lamination_rate_12mic")
            };
        }

        private static WasteRuleConfig MapWasteRules(List<estimate_config> rows)
        {
            int GetInt(string group, string key) =>
                (int)(rows.FirstOrDefault(x => x.config_group == group && x.config_key == key)?.value_num ?? 0);

            var printingByProductType = rows
                .Where(x => x.config_group == "wasteRules.printing.by_product_type")
                .ToDictionary(x => x.config_key, x => (int)(x.value_num ?? 0));

            return new WasteRuleConfig
            {
                Printing = new PrintingWasteConfig
                {
                    by_product_type = printingByProductType,
                    per_plate = GetInt("wasteRules.printing", "per_plate"),
                    @default = GetInt("wasteRules.printing", "default")
                },
                DieCutting = new StepWasteSimpleConfig
                {
                    lt_5000 = GetInt("wasteRules.dieCutting", "lt_5000"),
                    lt_20000 = GetInt("wasteRules.dieCutting", "lt_20000"),
                    ge_20000 = GetInt("wasteRules.dieCutting", "ge_20000")
                },
                Mounting = new StepWasteSimpleConfig
                {
                    lt_5000 = GetInt("wasteRules.mounting", "lt_5000"),
                    lt_20000 = GetInt("wasteRules.mounting", "lt_20000"),
                    ge_20000 = GetInt("wasteRules.mounting", "ge_20000")
                },
                Coating = new CoatingWasteConfig
                {
                    keo_nuoc = GetInt("wasteRules.coating", "keo_nuoc"),
                    keo_dau_lt_10000 = GetInt("wasteRules.coating", "keo_dau_lt_10000"),
                    keo_dau_ge_10000 = GetInt("wasteRules.coating", "keo_dau_ge_10000")
                },
                Lamination = new StepWasteSimpleConfig
                {
                    lt_5000 = GetInt("wasteRules.lamination", "lt_5000"),
                    lt_20000 = GetInt("wasteRules.lamination", "lt_20000"),
                    ge_20000 = GetInt("wasteRules.lamination", "ge_20000")
                },
                Gluing = new GluingWasteConfig
                {
                    lt_100 = GetInt("wasteRules.gluing", "lt_100"),
                    lt_500 = GetInt("wasteRules.gluing", "lt_500"),
                    lt_2000 = GetInt("wasteRules.gluing", "lt_2000"),
                    ge_2000 = GetInt("wasteRules.gluing", "ge_2000")
                }
            };
        }

        private static SystemConfig MapSystemParameters(List<estimate_config> rows)
        {
            decimal GetNum(string group, string key) =>
                rows.FirstOrDefault(x => x.config_group == group && x.config_key == key)?.value_num ?? 0;

            var rush = rows
                .Where(x => x.config_group == "systemParameters.rush_percent_by_days_early")
                .ToDictionary(
                    x => int.TryParse(x.config_key, out var day) ? day : 0,
                    x => x.value_num ?? 0
                );

            return new SystemConfig
            {
                default_production_days = (int)GetNum("systemParameters", "default_production_days"),
                rush_threshold_days = (int)GetNum("systemParameters", "rush_threshold_days"),
                vat_percent = GetNum("systemParameters", "vat_percent"),
                rush_percent_by_days_early = rush
            };
        }

        private static ProcessCostConfig MapProcessCosts(List<estimate_config> rows)
        {
            var byProcess = rows
                .Where(x => x.config_group == "processCosts.by_process")
                .OrderBy(x => x.config_key)
                .ToDictionary(
                    x => x.config_key,
                    x =>
                    {
                        var meta = ParseProcessCostMeta(x.value_json);

                        return new ProcessCostItemConfig
                        {
                            unit_price = x.value_num ?? 0,
                            unit = x.value_text ?? "",
                            note = meta.note
                        };
                    },
                    StringComparer.OrdinalIgnoreCase
                );

            return new ProcessCostConfig
            {
                by_process = byProcess
            };
        }

        private static DesignConfig MapDesign(List<estimate_config> rows)
        {
            decimal GetNum(string group, string key) =>
                rows.FirstOrDefault(x => x.config_group == group && x.config_key == key)?.value_num ?? 0;

            return new DesignConfig
            {
                default_design_cost = GetNum("design", "default_design_cost")
            };
        }

        private static ProcessCostMeta ParseProcessCostMeta(string? valueJson)
        {
            if (string.IsNullOrWhiteSpace(valueJson))
                return new ProcessCostMeta();

            try
            {
                return JsonSerializer.Deserialize<ProcessCostMeta>(valueJson) ?? new ProcessCostMeta();
            }
            catch
            {
                return new ProcessCostMeta();
            }
        }

        private sealed class ProcessCostMeta
        {
            public string process_name { get; set; } = "";
            public string note { get; set; } = "";
        }

        private static string GetPlateCategory(string key)
        {
            if (key.StartsWith("SMALL_", StringComparison.OrdinalIgnoreCase)) return "small";
            if (key.StartsWith("MEDIUM_", StringComparison.OrdinalIgnoreCase)) return "medium";
            if (key.StartsWith("LARGE_", StringComparison.OrdinalIgnoreCase)) return "large";
            if (key.StartsWith("XLARGE_", StringComparison.OrdinalIgnoreCase)) return "xlarge";
            return "";
        }

        private static string GetPlateCategoryText(string key)
        {
            if (key.StartsWith("SMALL_", StringComparison.OrdinalIgnoreCase)) return "Khổ nhỏ";
            if (key.StartsWith("MEDIUM_", StringComparison.OrdinalIgnoreCase)) return "Khổ trung";
            if (key.StartsWith("LARGE_", StringComparison.OrdinalIgnoreCase)) return "Khổ lớn";
            if (key.StartsWith("XLARGE_", StringComparison.OrdinalIgnoreCase)) return "Khổ siêu lớn";
            return "";
        }

        private static int GetPlateSortOrder(string key) => key switch
        {
            "SMALL_37X45" => 1,
            "SMALL_40X51" => 2,
            "SMALL_45X55" => 3,
            "MEDIUM_55X65" => 4,
            "MEDIUM_60_5X74_5" => 5,
            "MEDIUM_79X60" => 6,
            "LARGE_79X103" => 7,
            "LARGE_80X103" => 8,
            "XLARGE_114X145" => 9,
            "XLARGE_132X163" => 10,
            _ => 999
        };

        private static decimal GetPlateWidth(string key) => key switch
        {
            "SMALL_37X45" => 37,
            "SMALL_40X51" => 40,
            "SMALL_45X55" => 45,
            "MEDIUM_55X65" => 55,
            "MEDIUM_60_5X74_5" => 60.5m,
            "MEDIUM_79X60" => 79,
            "LARGE_79X103" => 79,
            "LARGE_80X103" => 80,
            "XLARGE_114X145" => 114,
            "XLARGE_132X163" => 132,
            _ => 0
        };

        private static decimal GetPlateHeight(string key) => key switch
        {
            "SMALL_37X45" => 45,
            "SMALL_40X51" => 51,
            "SMALL_45X55" => 55,
            "MEDIUM_55X65" => 65,
            "MEDIUM_60_5X74_5" => 74.5m,
            "MEDIUM_79X60" => 60,
            "LARGE_79X103" => 103,
            "LARGE_80X103" => 103,
            "XLARGE_114X145" => 145,
            "XLARGE_132X163" => 163,
            _ => 0
        };
    }
}