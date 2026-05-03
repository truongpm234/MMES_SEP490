using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMMS.Shared.Helpers
{
    public class MachineHelper
    {
        public static void AssignReservationToLane(List<DateTime> lanes, DateTime start, DateTime end)
        {
            var bestIndex = 0;
            var bestAvailable = lanes[0];

            for (var i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] <= start)
                {
                    bestIndex = i;
                    bestAvailable = lanes[i]; 
                    break;
                }

                if (lanes[i] < bestAvailable)
                {
                    bestIndex = i;
                    bestAvailable = lanes[i];
                }
            }

            var actualStart = bestAvailable > start ? bestAvailable : start;
            lanes[bestIndex] = end > actualStart ? end : actualStart;
        }

    }
}
