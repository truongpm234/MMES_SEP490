using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Interfaces;
using AMMS.Shared.DTOs.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Infrastructure.Repositories
{
    public class ProcessCostRuleRepository : IProcessCostRuleRepository
    {
        private readonly AppDbContext _db;

        public ProcessCostRuleRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<process_cost_rule>> GetAllAsync()
        {
            return await _db.process_cost_rules.ToListAsync();
        }
    }
}
