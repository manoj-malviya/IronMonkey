using IronMonkey.Api.Entities.Models;
using Microsoft.Net.Http.Headers;

namespace IronMonkey.Api.Entities.Leads;

public class LeadDetail {
    public int LeadId { get; set; }

    public IEnumerable<Field> Fields{ get; set; } = new List<Field>();
}