namespace IronMonkey.Api.Domain.Records;

public class RecordDetail {
    public int LeadId { get; set; }

    public IEnumerable<Field> Fields{ get; set; } = new List<Field>();
}